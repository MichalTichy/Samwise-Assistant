using System.Net;

namespace Samwise.Infrastructure.Teams.Graph;

internal sealed class TeamsGraphRequestException(HttpStatusCode statusCode, string message)
    : InvalidOperationException($"Microsoft Graph returned {(int)statusCode}: {message}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;

    public string GraphMessage { get; } = message;
}
