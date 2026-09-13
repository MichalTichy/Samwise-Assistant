namespace Samwise.Infrastructure.Email.Microsoft365;

internal sealed class MicrosoftTokenCacheState(byte[]? bytes = null)
{
    public byte[]? Bytes { get; set; } = bytes;
}
