using Microsoft.Identity.Client;

namespace Samwise.Infrastructure.Email.Microsoft365;

internal static class MicrosoftTokenCachePersistence
{
    public static void Attach(ITokenCache tokenCache, MicrosoftTokenCacheState state)
    {
        tokenCache.SetBeforeAccess(arguments =>
        {
            if (state.Bytes is { Length: > 0 })
            {
                arguments.TokenCache.DeserializeMsalV3(state.Bytes);
            }
        });
        tokenCache.SetAfterAccess(arguments =>
        {
            if (arguments.HasStateChanged)
            {
                state.Bytes = arguments.TokenCache.SerializeMsalV3();
            }
        });
    }
}
