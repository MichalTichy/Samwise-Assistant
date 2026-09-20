using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Samwise.Infrastructure.Teams.Configuration;
using Samwise.Inputs.Teams.Authentication;
using Samwise.Inputs.Teams.Configuration;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Teams.Authentication;

internal sealed class TeamsGraphTokenProvider : ITeamsAuthorizationService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ITeamsCredentialProtector credentialProtector;
    private readonly TeamsGraphRuntimeOptions options;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private readonly object stateLock = new();
    private ClientContext? currentContext;
    private Task? deviceCodeTask;
    private Task<TeamsAuthorizationChallenge>? challengeTask;
    private TeamsAuthorizationChallenge? pendingChallenge;
    private string? lastError;

    public TeamsGraphTokenProvider(
        IServiceScopeFactory scopeFactory,
        ITeamsCredentialProtector credentialProtector,
        IOptions<TeamsGraphRuntimeOptions> options)
    {
        this.scopeFactory = scopeFactory;
        this.credentialProtector = credentialProtector;
        this.options = options.Value;
        Directory.CreateDirectory(this.options.DataPath);
    }

    public async Task<string> GetDelegatedTokenAsync(CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        var account = (await context.PublicClient.GetAccountsAsync()).FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No Microsoft Teams account is connected. Connect it in Configuration → Inputs → Microsoft Teams.");
        try
        {
            var result = await context.PublicClient.AcquireTokenSilent(options.DelegatedScopes, account)
                .ExecuteAsync(cancellationToken);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException exception)
        {
            throw new InvalidOperationException(
                "The Microsoft Teams sign-in must be renewed in Configuration → Inputs → Microsoft Teams.",
                exception);
        }
    }

    public async Task<string> GetApplicationTokenAsync(CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        if (context.ConfidentialClient is null)
        {
            throw new InvalidOperationException(
                "Teams transcript intake requires a client secret in Configuration → Inputs → Microsoft Teams.");
        }

        var result = await context.ConfidentialClient
            .AcquireTokenForClient(["https://graph.microsoft.com/.default"])
            .ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }

    public async Task<TeamsAuthorizationChallenge> StartAsync(CancellationToken cancellationToken = default)
    {
        var context = await GetContextAsync(cancellationToken);
        Task<TeamsAuthorizationChallenge> currentChallengeTask;
        lock (stateLock)
        {
            if (deviceCodeTask is { IsCompleted: false } && challengeTask is not null)
            {
                currentChallengeTask = challengeTask;
            }
            else
            {
                pendingChallenge = null;
                lastError = null;
                var challengeSource = new TaskCompletionSource<TeamsAuthorizationChallenge>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                challengeTask = challengeSource.Task;
                deviceCodeTask = CompleteDeviceCodeSignInAsync(context, challengeSource);
                currentChallengeTask = challengeTask;
            }
        }

        return await currentChallengeTask.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }

    public async Task<TeamsConnectionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await GetContextAsync(cancellationToken);
            var account = (await context.PublicClient.GetAccountsAsync()).FirstOrDefault();
            lock (stateLock)
            {
                return new TeamsConnectionState(
                    account is not null,
                    account?.Username,
                    pendingChallenge,
                    lastError);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new TeamsConnectionState(false, null, null, exception.Message);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ClientContext? context;
        lock (stateLock)
        {
            context = currentContext;
        }

        if (context is not null)
        {
            foreach (var account in await context.PublicClient.GetAccountsAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                await context.PublicClient.RemoveAsync(account);
            }

            if (File.Exists(context.CachePath))
            {
                File.Delete(context.CachePath);
            }
        }

        lock (stateLock)
        {
            pendingChallenge = null;
            lastError = null;
            challengeTask = null;
        }
    }

    private async Task<ClientContext> GetContextAsync(CancellationToken cancellationToken)
    {
        var registration = await LoadRegistrationAsync(cancellationToken);
        lock (stateLock)
        {
            if (currentContext?.Registration == registration)
            {
                return currentContext;
            }

            currentContext = CreateContext(registration);
            pendingChallenge = null;
            lastError = null;
            challengeTask = null;
            deviceCodeTask = null;
            return currentContext;
        }
    }

    private async Task<Registration> LoadRegistrationAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReadOnlyRepository<TeamsInputConfiguration>>();
        var configuration = await repository.GetRequiredByIdAsync(
            TeamsInputConfiguration.DocumentId,
            cancellationToken);
        var graph = configuration.MicrosoftGraph;
        if (string.IsNullOrWhiteSpace(graph.TenantId) || string.IsNullOrWhiteSpace(graph.ClientId))
        {
            throw new InvalidOperationException(
                "Configure the Microsoft tenant ID and application client ID before connecting Teams.");
        }

        var clientSecret = graph.HasClientSecret
            ? credentialProtector.Unprotect(graph.ProtectedClientSecret!)
            : null;
        return new Registration(graph.TenantId.Trim(), graph.ClientId.Trim(), clientSecret);
    }

    private ClientContext CreateContext(Registration registration)
    {
        var cacheKey = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"{registration.TenantId}\n{registration.ClientId}")))[..16];
        var cachePath = Path.Combine(options.DataPath, $"delegated-token-cache-{cacheKey}.bin");
        var publicClient = PublicClientApplicationBuilder.Create(registration.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{registration.TenantId}")
            .Build();
        RegisterPersistentCache(publicClient.UserTokenCache, cachePath);

        var confidentialClient = string.IsNullOrWhiteSpace(registration.ClientSecret)
            ? null
            : ConfidentialClientApplicationBuilder.Create(registration.ClientId)
                .WithAuthority($"https://login.microsoftonline.com/{registration.TenantId}")
                .WithClientSecret(registration.ClientSecret)
                .Build();
        return new ClientContext(registration, publicClient, confidentialClient, cachePath);
    }

    private async Task CompleteDeviceCodeSignInAsync(
        ClientContext context,
        TaskCompletionSource<TeamsAuthorizationChallenge> challengeSource)
    {
        try
        {
            await context.PublicClient.AcquireTokenWithDeviceCode(options.DelegatedScopes, result =>
            {
                var challenge = new TeamsAuthorizationChallenge(
                    result.UserCode,
                    result.VerificationUrl,
                    result.Message,
                    result.ExpiresOn);
                lock (stateLock)
                {
                    if (ReferenceEquals(currentContext, context))
                    {
                        pendingChallenge = challenge;
                    }
                }

                challengeSource.TrySetResult(challenge);
                return Task.CompletedTask;
            }).ExecuteAsync();
            lock (stateLock)
            {
                if (ReferenceEquals(currentContext, context))
                {
                    pendingChallenge = null;
                }
            }
        }
        catch (Exception exception)
        {
            lock (stateLock)
            {
                if (ReferenceEquals(currentContext, context))
                {
                    pendingChallenge = null;
                    lastError = exception.GetBaseException().Message;
                }
            }

            challengeSource.TrySetException(exception);
        }
    }

    private void RegisterPersistentCache(ITokenCache cache, string cachePath)
    {
        cache.SetBeforeAccessAsync(async args =>
        {
            await cacheLock.WaitAsync();
            try
            {
                if (File.Exists(cachePath))
                {
                    args.TokenCache.DeserializeMsalV3(
                        await File.ReadAllBytesAsync(cachePath),
                        shouldClearExistingCache: true);
                }
            }
            finally
            {
                cacheLock.Release();
            }
        });
        cache.SetAfterAccessAsync(async args =>
        {
            if (!args.HasStateChanged)
            {
                return;
            }

            await cacheLock.WaitAsync();
            try
            {
                var temporaryPath = cachePath + ".tmp";
                await File.WriteAllBytesAsync(temporaryPath, args.TokenCache.SerializeMsalV3());
                File.Move(temporaryPath, cachePath, overwrite: true);
            }
            finally
            {
                cacheLock.Release();
            }
        });
    }

    private sealed record Registration(string TenantId, string ClientId, string? ClientSecret);

    private sealed record ClientContext(
        Registration Registration,
        IPublicClientApplication PublicClient,
        IConfidentialClientApplication? ConfidentialClient,
        string CachePath);
}
