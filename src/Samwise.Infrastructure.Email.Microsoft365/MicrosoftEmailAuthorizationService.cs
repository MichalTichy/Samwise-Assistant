using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Samwise.Inputs.Email;

namespace Samwise.Infrastructure.Email.Microsoft365;

internal sealed class MicrosoftEmailAuthorizationService(
    IOptions<Microsoft365EmailOptions> options,
    TimeProvider timeProvider) : IMicrosoftEmailAuthorizationService
{
    private readonly ConcurrentDictionary<string, AuthorizationSession> sessions = new();

    public async Task<MicrosoftEmailAuthorizationChallenge> StartAsync(
        CancellationToken cancellationToken)
    {
        RemoveExpiredSessions();
        var settings = GetValidatedOptions();
        var application = PublicClientApplicationBuilder
            .Create(settings.ClientId)
            .WithAuthority($"https://login.microsoftonline.com/{settings.TenantId}")
            .Build();
        var tokenCache = new MicrosoftTokenCacheState();
        MicrosoftTokenCachePersistence.Attach(application.UserTokenCache, tokenCache);
        var challengeSource = new TaskCompletionSource<DeviceCodeResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var authorizationCancellation = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        var authorizationTask = application
            .AcquireTokenWithDeviceCode(
                Microsoft365EmailScopes.Values,
                result =>
                {
                    challengeSource.TrySetResult(result);
                    return Task.CompletedTask;
                })
            .ExecuteAsync(authorizationCancellation.Token);

        Task firstCompleted;
        try
        {
            firstCompleted = await Task.WhenAny(challengeSource.Task, authorizationTask)
                .WaitAsync(cancellationToken);
            if (firstCompleted == authorizationTask)
            {
                await authorizationTask;
                throw new InvalidOperationException(
                    "Microsoft sign-in finished without returning a device-code challenge.");
            }
        }
        catch
        {
            authorizationCancellation.Cancel();
            authorizationCancellation.Dispose();
            throw;
        }

        var deviceCode = await challengeSource.Task;
        var authorizationId = Guid.NewGuid().ToString("N");
        sessions[authorizationId] = new AuthorizationSession(
            application,
            authorizationTask,
            authorizationCancellation,
            deviceCode.ExpiresOn,
            tokenCache);

        return new MicrosoftEmailAuthorizationChallenge(
            authorizationId,
            deviceCode.UserCode,
            deviceCode.VerificationUrl,
            deviceCode.Message,
            deviceCode.ExpiresOn);
    }

    public async Task<MicrosoftEmailAuthorizationCompletion> CompleteAsync(
        string authorizationId,
        CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(authorizationId, out var session))
        {
            return new MicrosoftEmailAuthorizationCompletion(
                MicrosoftEmailAuthorizationState.Expired,
                null,
                null,
                null,
                "The Microsoft sign-in request expired. Start a new sign-in.");
        }

        if (timeProvider.GetUtcNow() >= session.ExpiresAt)
        {
            RemoveSession(authorizationId, session);
            return new MicrosoftEmailAuthorizationCompletion(
                MicrosoftEmailAuthorizationState.Expired,
                null,
                null,
                null,
                "The Microsoft sign-in request expired. Start a new sign-in.");
        }

        if (!session.AuthorizationTask.IsCompleted)
        {
            return new MicrosoftEmailAuthorizationCompletion(
                MicrosoftEmailAuthorizationState.Pending,
                null,
                null,
                null,
                null);
        }

        try
        {
            var result = await session.AuthorizationTask.WaitAsync(cancellationToken);
            var tokenCache = Convert.ToBase64String(
                session.TokenCache.Bytes
                ?? throw new InvalidOperationException("Microsoft did not return a reusable session."));
            return new MicrosoftEmailAuthorizationCompletion(
                MicrosoftEmailAuthorizationState.Succeeded,
                tokenCache,
                result.Account.HomeAccountId.Identifier,
                result.Account.Username,
                null);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            RemoveSession(authorizationId, session, cancel: false);
            return new MicrosoftEmailAuthorizationCompletion(
                MicrosoftEmailAuthorizationState.Failed,
                null,
                null,
                null,
                $"Microsoft sign-in failed: {exception.GetBaseException().Message}");
        }
    }

    public void Finish(string authorizationId)
    {
        if (sessions.TryGetValue(authorizationId, out var session))
        {
            RemoveSession(authorizationId, session, cancel: false);
        }
    }

    private Microsoft365EmailOptions GetValidatedOptions()
    {
        var settings = options.Value;
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            throw new InvalidOperationException(
                "Microsoft 365 email sign-in is not configured. Set Email:Microsoft365:ClientId.");
        }

        if (string.IsNullOrWhiteSpace(settings.TenantId))
        {
            throw new InvalidOperationException(
                "Microsoft 365 email sign-in is not configured. Set Email:Microsoft365:TenantId.");
        }

        return settings;
    }

    private void RemoveExpiredSessions()
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (authorizationId, session) in sessions)
        {
            if (now >= session.ExpiresAt)
            {
                RemoveSession(authorizationId, session);
            }
        }
    }

    private void RemoveSession(
        string authorizationId,
        AuthorizationSession session,
        bool cancel = true)
    {
        sessions.TryRemove(authorizationId, out _);
        if (cancel && !session.AuthorizationTask.IsCompleted)
        {
            session.Cancellation.Cancel();
        }

        session.Cancellation.Dispose();
    }

    private sealed record AuthorizationSession(
        IPublicClientApplication Application,
        Task<AuthenticationResult> AuthorizationTask,
        CancellationTokenSource Cancellation,
        DateTimeOffset ExpiresAt,
        MicrosoftTokenCacheState TokenCache);
}
