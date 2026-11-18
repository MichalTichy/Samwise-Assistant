using System.Net;

namespace Samwise.Infrastructure.Todoist.Api;

internal sealed class TodoistApiException(HttpStatusCode statusCode, string message) : InvalidOperationException(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}
