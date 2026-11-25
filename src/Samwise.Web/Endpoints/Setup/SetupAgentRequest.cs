namespace Samwise.Web.Endpoints.Setup;

public sealed record SetupAgentRequest(string Model, string ReasoningEffort, string? Prompt = null);
