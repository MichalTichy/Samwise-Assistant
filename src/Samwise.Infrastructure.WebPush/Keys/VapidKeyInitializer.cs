using System.Buffers.Text;
using System.Security.Cryptography;
using Samwise.Logic.Notifications;
using Shared.Infrastructure.Initializer;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Infrastructure.WebPush.Keys;

/// <summary>
/// Creates this instance's VAPID key pair once. Replacing it would invalidate every existing browser
/// subscription, so an existing document is never regenerated.
/// </summary>
internal sealed class VapidKeyInitializer(
    IRepository<PushApplicationKeys> repository,
    VapidKeyProtector protector,
    TimeProvider timeProvider) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override async Task RunInitializationLogicAsync()
    {
        if (await repository.GetByIdAsync(PushApplicationKeys.DocumentId) is not null)
        {
            return;
        }

        var (publicKey, privateKey) = GenerateKeyPair();
        await repository.AddAsync(new PushApplicationKeys
        {
            PublicKey = publicKey,
            ProtectedPrivateKey = protector.Protect(privateKey),
            CreatedAt = timeProvider.GetUtcNow()
        });
    }

    internal static (string PublicKey, string PrivateKey) GenerateKeyPair()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        var publicKey = new byte[65];
        publicKey[0] = 0x04;
        parameters.Q.X!.CopyTo(publicKey, 1);
        parameters.Q.Y!.CopyTo(publicKey, 33);
        return (Base64Url.EncodeToString(publicKey), Base64Url.EncodeToString(parameters.D!));
    }
}
