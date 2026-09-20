using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Samwise.Infrastructure.Teams.Configuration;
using Samwise.Inputs.Teams;

namespace Samwise.Infrastructure.Teams.Authentication;

internal sealed class TeamsGraphTokenProvider : ITeamsAuthorizationService
{
    private readonly TeamsMicrosoftGraphOptions options;
    private readonly IPublicClientApplication publicClient;
    private readonly IConfidentialClientApplication? confidentialClient;
    private readonly string cachePath;
    private readonly SemaphoreSlim cacheLock = new(1, 1);
    private readonly object stateLock = new();
    private Task? deviceCodeTask;
    private Task<TeamsAuthorizationChallenge>? challengeTask;
    private TeamsAuthorizationChallenge? pendingChallenge;
    private string? lastError;

    public TeamsGraphTokenProvider(IOptions<TeamsMicrosoftGraphOptions> options)
    {
        this.options = options.Value;
        Directory.CreateDirectory(this.options.DataPath);
        cachePath = Path.Combine(this.options.DataPath, "delegated-token-cache.bin");
        publicClient = PublicClientApplicationBuilder.Create(this.options.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{this.options.TenantId}")
            .Build();
        RegisterPersistentCache(publicClient.UserTokenCache);

        if (!string.IsNullOrWhiteSpace(this.options.ClientSecret))
        {
            confidentialClient = ConfidentialClientApplicationBuilder.Create(this.options.ClientId)
                .WithAuthority($"https://login.microsoftonline.com/{this.options.TenantId}")
                .WithClientSecret(this.options.ClientSecret)
                .Build();
        }
    }

    public async Task<string> GetDelegatedTokenAsync(CancellationToken cancellationToken)
    {
        var account = (await publicClient.GetAccountsAsync()).FirstOrDefault()
            ?? throw new InvalidOperationException(
                "No Microsoft Teams account is connected. Connect it in Configuration → Inputs → Microsoft Teams.");
        try
        {
            var result = await publicClient.AcquireTokenSilent(options.DelegatedScopes, account)
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
        if (confidentialClient is null)
        {
            throw new InvalidOperationException(
                "Teams transcript intake requires TeamsInput:MicrosoftGraph:ClientSecret.");
        }

        var result = await confidentialClient
            .AcquireTokenForClient(["https://graph.microsoft.com/.default"])
            .ExecuteAsync(cancellationToken);
        return result.AccessToken;
    }

    public async Task<TeamsAuthorizationChallenge> StartAsync(CancellationToken cancellationToken = default)
    {
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
                deviceCodeTask = CompleteDeviceCodeSignInAsync(challengeSource);
                currentChallengeTask = challengeTask;
            }
        }

        return await currentChallengeTask.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
    }

    public async Task<TeamsConnectionState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var account = (await publicClient.GetAccountsAsync()).FirstOrDefault();
        lock (stateLock)
        {
            return new TeamsConnectionState(
                account is not null,
                account?.Username,
                pendingChallenge,
                lastError);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        foreach (var account in await publicClient.GetAccountsAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await publicClient.RemoveAsync(account);
        }

        if (File.Exists(cachePath))
        {
            File.Delete(cachePath);
        }

        lock (stateLock)
        {
            pendingChallenge = null;
            lastError = null;
            challengeTask = null;
        }
    }

    private async Task CompleteDeviceCodeSignInAsync(
        TaskCompletionSource<TeamsAuthorizationChallenge> challengeSource)
    {
        try
        {
            await publicClient.AcquireTokenWithDeviceCode(options.DelegatedScopes, result =>
            {
                var challenge = new TeamsAuthorizationChallenge(
                    result.UserCode,
                    result.VerificationUrl,
                    result.Message,
                    result.ExpiresOn);
                lock (stateLock)
                {
                    pendingChallenge = challenge;
                }

                challengeSource.TrySetResult(challenge);
                return Task.CompletedTask;
            }).ExecuteAsync();
            lock (stateLock)
            {
                pendingChallenge = null;
            }
        }
        catch (Exception exception)
        {
            lock (stateLock)
            {
                pendingChallenge = null;
                lastError = exception.GetBaseException().Message;
            }

            challengeSource.TrySetException(exception);
        }
    }

    private void RegisterPersistentCache(ITokenCache cache)
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
}
