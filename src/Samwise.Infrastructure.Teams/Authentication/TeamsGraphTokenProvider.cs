using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Samwise.Inputs.Teams.Authentication;
using Samwise.Inputs.Teams.Configuration;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.Teams.Authentication;

internal sealed class TeamsGraphTokenProvider : ITeamsAuthorizationService
{
    private static readonly string[] DelegatedScopes =
    [
        "User.Read",
        "Chat.Read",
        "Calendars.Read",
        "OnlineMeetings.Read",
        "OnlineMeetingTranscript.Read.All",
        "Team.ReadBasic.All",
        "Channel.ReadBasic.All",
        "ChannelMessage.Read.All"
    ];

    private readonly IServiceScopeFactory scopeFactory;
    private readonly ITeamsCredentialProtector credentialProtector;
    private readonly string cacheDirectory;
    private readonly object stateLock = new();
    private ClientContext? currentContext;

    public TeamsGraphTokenProvider(
        IServiceScopeFactory scopeFactory,
        ITeamsCredentialProtector credentialProtector,
        IConfiguration configuration)
    {
        this.scopeFactory = scopeFactory;
        this.credentialProtector = credentialProtector;
        var dataProtectionPath = configuration["DataProtection:KeysPath"]
            ?? Path.Combine(Directory.GetCurrentDirectory(), ".data", "data-protection");
        cacheDirectory = Path.Combine(dataProtectionPath, "teams");
        Directory.CreateDirectory(cacheDirectory);
    }

    public async Task<string> GetDelegatedTokenAsync(CancellationToken cancellationToken)
    {
        var context = await GetContextAsync(cancellationToken);
        var account = (await context.PublicClient.GetAccountsAsync()).FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No Microsoft Teams account is connected. Connect it in Configuration → Inputs → Microsoft Teams.");
        try
        {
            var result = await context.PublicClient.AcquireTokenSilent(DelegatedScopes, account)
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
        AuthorizationAttempt attempt;
        lock (stateLock)
        {
            if (context.AuthorizationAttempt is { IsRunning: true } currentAttempt)
            {
                attempt = currentAttempt;
            }
            else
            {
                attempt = new AuthorizationAttempt();
                context.AuthorizationAttempt = attempt;
                attempt.Completion = CompleteDeviceCodeSignInAsync(context.PublicClient, attempt);
            }
        }

        return await attempt.ChallengeTask.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }

    public async Task<TeamsConnectionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var context = await GetContextAsync(cancellationToken);
            var account = (await context.PublicClient.GetAccountsAsync()).FirstOrDefault();
            lock (stateLock)
            {
                var attempt = context.AuthorizationAttempt;
                return new TeamsConnectionState(
                    account is not null,
                    account?.Username,
                    attempt?.Challenge,
                    attempt?.Error);
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
            if (context is not null)
            {
                context.AuthorizationAttempt = null;
            }
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
        var cachePath = Path.Combine(cacheDirectory, $"delegated-token-cache-{cacheKey}.bin");
        var publicClient = PublicClientApplicationBuilder.Create(registration.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{registration.TenantId}")
            .Build();
        RegisterPersistentCache(publicClient.UserTokenCache, cachePath, new SemaphoreSlim(1, 1));

        var confidentialClient = string.IsNullOrWhiteSpace(registration.ClientSecret)
            ? null
            : ConfidentialClientApplicationBuilder.Create(registration.ClientId)
                .WithAuthority($"https://login.microsoftonline.com/{registration.TenantId}")
                .WithClientSecret(registration.ClientSecret)
                .Build();
        return new ClientContext(registration, publicClient, confidentialClient, cachePath);
    }

    private async Task CompleteDeviceCodeSignInAsync(
        IPublicClientApplication publicClient,
        AuthorizationAttempt attempt)
    {
        try
        {
            await publicClient.AcquireTokenWithDeviceCode(DelegatedScopes, result =>
            {
                var challenge = new TeamsAuthorizationChallenge(
                    result.UserCode,
                    result.VerificationUrl,
                    result.Message,
                    result.ExpiresOn);
                attempt.PublishChallenge(challenge);
                return Task.CompletedTask;
            }).ExecuteAsync();
            attempt.Complete();
        }
        catch (Exception exception)
        {
            attempt.Fail(exception);
        }
    }

    private static void RegisterPersistentCache(
        ITokenCache cache,
        string cachePath,
        SemaphoreSlim cacheLock)
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

    private sealed class ClientContext(
        Registration registration,
        IPublicClientApplication publicClient,
        IConfidentialClientApplication? confidentialClient,
        string cachePath)
    {
        public Registration Registration { get; } = registration;
        public IPublicClientApplication PublicClient { get; } = publicClient;
        public IConfidentialClientApplication? ConfidentialClient { get; } = confidentialClient;
        public string CachePath { get; } = cachePath;
        public AuthorizationAttempt? AuthorizationAttempt { get; set; }
    }

    private sealed class AuthorizationAttempt
    {
        private readonly object sync = new();
        private readonly TaskCompletionSource<TeamsAuthorizationChallenge> challengeSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private TeamsAuthorizationChallenge? challenge;
        private string? error;

        public Task<TeamsAuthorizationChallenge> ChallengeTask => challengeSource.Task;
        public Task Completion { get; set; } = Task.CompletedTask;
        public TeamsAuthorizationChallenge? Challenge
        {
            get
            {
                lock (sync)
                {
                    return challenge;
                }
            }
        }

        public string? Error
        {
            get
            {
                lock (sync)
                {
                    return error;
                }
            }
        }

        public bool IsRunning => !Completion.IsCompleted;

        public void PublishChallenge(TeamsAuthorizationChallenge challenge)
        {
            lock (sync)
            {
                this.challenge = challenge;
            }

            challengeSource.TrySetResult(challenge);
        }

        public void Complete()
        {
            lock (sync)
            {
                challenge = null;
            }
        }

        public void Fail(Exception exception)
        {
            lock (sync)
            {
                challenge = null;
                error = exception.GetBaseException().Message;
            }

            challengeSource.TrySetException(exception);
        }
    }
}
