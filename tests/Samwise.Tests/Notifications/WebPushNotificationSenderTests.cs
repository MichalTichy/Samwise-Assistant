using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using Lib.Net.Http.WebPush;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Samwise.Domain.Notifications;
using Samwise.Infrastructure.WebPush;
using Samwise.Infrastructure.WebPush.Keys;
using Samwise.Infrastructure.WebPush.Sending;
using Samwise.Logic.Notifications;
using Samwise.Logic.Notifications.Services;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Tests.Notifications;

public sealed class WebPushNotificationSenderTests
{
    [Fact]
    public void GeneratedVapidKeysHaveTheP256WireFormat()
    {
        var (publicKey, privateKey) = VapidKeyInitializer.GenerateKeyPair();

        var publicBytes = Base64Url.DecodeFromChars(publicKey);
        Assert.Equal(65, publicBytes.Length);
        Assert.Equal(0x04, publicBytes[0]);
        Assert.Equal(32, Base64Url.DecodeFromChars(privateKey).Length);
    }

    [Fact]
    public async Task DeliveredMessageIsEncryptedAndSignedForThePushService()
    {
        var handler = new StubHandler(HttpStatusCode.Created);
        var sender = CreateSender(handler, subject: "mailto:owner@example.com");

        var result = await sender.SendAsync(Device(), Message(urgent: true), CancellationToken.None);

        Assert.Equal(PushDeliveryStatus.Delivered, result.Status);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://web.push.apple.com/device-token", request.RequestUri!.ToString());
        Assert.Equal("vapid", request.Headers.Authorization?.Scheme);
        Assert.Equal("86400", request.Headers.GetValues("TTL").Single());
        Assert.Equal("high", request.Headers.GetValues("Urgency").Single());
        Assert.Equal("aes128gcm", request.ContentEncoding);
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task UnknownSubscriptionIsReportedAsExpired(HttpStatusCode status)
    {
        var sender = CreateSender(new StubHandler(status), subject: "mailto:owner@example.com");

        var result = await sender.SendAsync(Device(), Message(urgent: false), CancellationToken.None);

        Assert.Equal(PushDeliveryStatus.Expired, result.Status);
    }

    [Fact]
    public async Task OwnerEmailIsTheDefaultSubjectAndAMissingOneFailsWithoutSending()
    {
        var handler = new StubHandler(HttpStatusCode.Created);

        var withOwner = await CreateSender(handler, subject: null, ownerEmail: "owner@example.com")
            .SendAsync(Device(), Message(urgent: false), CancellationToken.None);
        var withoutSubject = await CreateSender(handler, subject: null, ownerEmail: null)
            .SendAsync(Device(), Message(urgent: false), CancellationToken.None);

        Assert.Equal(PushDeliveryStatus.Delivered, withOwner.Status);
        Assert.Equal(PushDeliveryStatus.Failed, withoutSubject.Status);
        Assert.Single(handler.Requests);
    }

    private static WebPushNotificationSender CreateSender(StubHandler handler, string? subject, string? ownerEmail = null)
    {
        var protector = new VapidKeyProtector(new EphemeralDataProtectionProvider());
        var (publicKey, privateKey) = VapidKeyInitializer.GenerateKeyPair();
        var keys = new PushApplicationKeys { PublicKey = publicKey, ProtectedPrivateKey = protector.Protect(privateKey) };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OwnerAccount:Email"] = ownerEmail })
            .Build();
        return new WebPushNotificationSender(
            new PushServiceClient(new HttpClient(handler)),
            new KeyRepository(keys),
            protector,
            Options.Create(new WebPushOptions { Subject = subject }),
            configuration);
    }

    private static PushDevice Device()
    {
        // A real browser key pair, so the library can encrypt the payload.
        using var browserKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var parameters = browserKey.ExportParameters(includePrivateParameters: false);
        var publicKey = new byte[65];
        publicKey[0] = 0x04;
        parameters.Q.X!.CopyTo(publicKey, 1);
        parameters.Q.Y!.CopyTo(publicKey, 33);
        return new PushDevice
        {
            Id = "device",
            Name = "iPhone",
            Endpoint = "https://web.push.apple.com/device-token",
            P256dh = Base64Url.EncodeToString(publicKey),
            Auth = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16))
        };
    }

    private static PushNotification Message(bool urgent) =>
        new("Plan ready for approval", "Send Rosie the offer", "/work-items/wi-1", "workitem-wi-1", urgent);

    private sealed class StubHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri,
                request.Headers,
                request.Content?.Headers.ContentEncoding.SingleOrDefault()));
            return Task.FromResult(new HttpResponseMessage(status) { RequestMessage = request });
        }
    }

    private sealed record CapturedRequest(
        HttpMethod Method,
        Uri? RequestUri,
        System.Net.Http.Headers.HttpRequestHeaders Headers,
        string? ContentEncoding);

    private sealed class KeyRepository(PushApplicationKeys keys) : IReadOnlyRepository<PushApplicationKeys>
    {
        public Task<PushApplicationKeys?> GetByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) =>
            Task.FromResult<PushApplicationKeys?>(id == keys.Id ? keys : null);

        public Task<IReadOnlyList<PushApplicationKeys>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<PushApplicationKeys?> GetBySpecAsync(ISpecification<PushApplicationKeys> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<PushApplicationKeys, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PushApplicationKeys>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PushApplicationKeys>> ListAsync(IListSpecification<PushApplicationKeys> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<PushApplicationKeys, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();
    }
}
