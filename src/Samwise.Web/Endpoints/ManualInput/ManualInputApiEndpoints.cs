using System.Security.Cryptography;
using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Samwise.Inputs.Manual.Usecases;

namespace Samwise.Web.Endpoints.ManualInput;

public static class ManualInputApiEndpoints
{
    public const string ApiKeyHeaderName = "X-Samwise-Input-Key";

    public static IEndpointConventionBuilder MapManualInputApi(this IEndpointRouteBuilder endpoints)
    {
        return endpoints.MapPost("/api/manual-input", SubmitAsync)
            .AllowAnonymous()
            .DisableAntiforgery();
    }

    private static async Task<IResult> SubmitAsync(
        HttpContext context,
        [FromQuery(Name = "type")] string? inputType,
        ManualInputApiRequest request,
        IOptions<ManualInputApiOptions> options,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var expectedApiKey = options.Value.ApiKey;
        if (string.IsNullOrWhiteSpace(expectedApiKey))
        {
            return Results.Problem(
                "Manual input API is not configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var headerValues)
            || headerValues.Count != 1
            || !KeysMatch(expectedApiKey, headerValues[0]))
        {
            return Results.Unauthorized();
        }

        var errors = Validate(inputType, request);
        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var inboundEventId = await sender.Send(new SubmitManualInputUsecase(
            request.Text,
            inputType,
            request.Title,
            request.Url,
            request.Note), cancellationToken);

        return Results.Accepted("/inbox", new ManualInputApiResponse(inboundEventId));
    }

    private static Dictionary<string, string[]> Validate(
        string? inputType,
        ManualInputApiRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        AddRequiredTextError(errors, request.Text);
        AddLengthError(errors, "type", inputType, 100);
        AddLengthError(errors, "title", request.Title, 500);
        AddLengthError(errors, "url", request.Url, 2_048);
        AddLengthError(errors, "note", request.Note, 5_000);
        return errors;
    }

    private static void AddRequiredTextError(
        IDictionary<string, string[]> errors,
        string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            errors["text"] = ["Text is required."];
        }
        else if (text.Length > 20_000)
        {
            errors["text"] = ["Text cannot exceed 20,000 characters."];
        }
    }

    private static void AddLengthError(
        IDictionary<string, string[]> errors,
        string key,
        string? value,
        int maximumLength)
    {
        if (value?.Length > maximumLength)
        {
            errors[key] = [$"{key} cannot exceed {maximumLength:N0} characters."];
        }
    }

    private static bool KeysMatch(string expected, string? provided)
    {
        if (string.IsNullOrWhiteSpace(provided))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }

    public sealed record ManualInputApiRequest(
        string Text,
        string? Title = null,
        string? Url = null,
        string? Note = null);

    public sealed record ManualInputApiResponse(string InputEventId);
}
