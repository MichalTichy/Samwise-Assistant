using MediatR;
using Microsoft.Extensions.Options;
using Samwise.Domain.Agents;
using Samwise.Domain.Inbox;
using Samwise.Domain.WorkItems;
using Samwise.Domain.Playbooks;
using Samwise.Infrastructure.AgentRuntimes.Agents;
using Samwise.Inputs.Email;
using Samwise.Inputs.Email.Specifications;
using Samwise.Inputs.Email.Usecases;
using Samwise.Inputs.Teams.Configuration;
using Samwise.Inputs.Teams.Usecases.Configuration;
using Samwise.Inputs.Teams.Usecases.Authorization;
using Samwise.Inputs.Teams.Authentication;
using Samwise.Inputs.Todoist.Configuration;
using Samwise.Inputs.Todoist.Usecases.Configuration;
using Samwise.Logic.Agents;
using Samwise.Logic.Inbox.Specifications;
using Samwise.Logic.WorkItems.Specifications;
using Samwise.Logic.Playbooks.Specifications;
using Samwise.Logic.Playbooks.Usecases;
using Samwise.Logic.Prompts;
using Samwise.Logic.Prompts.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Endpoints.Setup;

public static class SetupEndpointExtensions
{
    public static void MapSamwiseSetupApi(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/setup")
            .AllowAnonymous()
            .AddEndpointFilter<SetupAccessFilter>();

        group.MapGet("/status", GetStatusAsync);
        group.MapGet("/agents", GetAgentsAsync);
        group.MapGet("/agents/{stage}/models", GetModelsAsync);
        group.MapPut("/agents/{stage}", SaveAgentAsync);
        group.MapGet("/playbooks", async (IReadOnlyRepository<Playbook> repository, CancellationToken ct) =>
            Results.Ok(await repository.ListAsync(new PlaybooksSpecification(), ct)));
        group.MapPost("/playbooks", async (SavePlaybookUsecase request, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(request, ct)));
        group.MapGet("/inputs", GetInputsAsync);
        group.MapPut("/inputs/todoist", async (
            SaveTodoistInputConfigurationUsecase request, ISender sender, CancellationToken ct) =>
            request.IgnoredProjects is null || request.IgnoredLabels is null
                ? Results.BadRequest(new { error = "ignoredProjects and ignoredLabels arrays are required." }) as IResult
                : Results.Ok(await sender.Send(request, ct)));
        group.MapPut("/inputs/teams", async (
            SaveTeamsInputConfigurationUsecase request, ISender sender, CancellationToken ct) =>
            request.IgnoredPeople is null || request.IgnoredChats is null || request.IgnoredChannels is null
                ? Results.BadRequest(new { error = "ignoredPeople, ignoredChats and ignoredChannels arrays are required." }) as IResult
                : Results.Ok(await sender.Send(request, ct)));
        group.MapPut("/inputs/email", async (
            SaveEmailInputConfigurationUsecase request, ISender sender, CancellationToken ct) =>
            request.IgnoredSenderAddresses is null
                ? Results.BadRequest(new { error = "ignoredSenderAddresses array is required." }) as IResult
                : Results.Ok(await sender.Send(request, ct)));
        group.MapPost("/inputs/email/accounts", async (
            SaveEmailAccountUsecase request, ISender sender, CancellationToken ct) =>
            !Enum.IsDefined(request.Security) || !Enum.IsDefined(request.AuthenticationMethod)
                ? Results.BadRequest(new { error = "Unknown email security or authentication method." }) as IResult
                : Results.Ok(await sender.Send(request, ct)));
        group.MapPost("/inputs/email/authorize", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new StartMicrosoftEmailAuthorizationUsecase(), ct)));
        group.MapPost("/inputs/teams/authorize", async (ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new StartTeamsAuthorizationUsecase(), ct)));
        group.MapGet("/inputs/teams/authorization", async (ITeamsAuthorizationService authorization, CancellationToken ct) =>
            Results.Ok(await authorization.GetStateAsync(ct)));
        group.MapGet("/input-events", GetInputEventsAsync);
        group.MapGet("/work-items", GetWorkItemsAsync);
    }

    private static async Task<IResult> GetStatusAsync(
        IReadOnlyRepository<AgentConfiguration> agents,
        IOptions<AgentEndpointOptions> endpointOptions,
        IConfiguration configuration,
        CancellationToken ct)
    {
        var settings = await agents.GetByIdAsync(AgentConfiguration.DocumentId, ct);
        var options = endpointOptions.Value;
        return Results.Ok(new
        {
            setupApiVersion = 1,
            manualInputConfigured = !string.IsNullOrWhiteSpace(configuration["ManualInputApi:ApiKey"]),
            mcpConfigured = !string.IsNullOrWhiteSpace(configuration["SamwiseMcp:ApiKey"]),
            stages = Enum.GetValues<AgentStage>().Select(stage => new
            {
                stage = stage.ToString(),
                model = settings?.GetStage(stage).Model,
                reasoningEffort = settings?.GetStage(stage).ReasoningEffort,
                endpointConfigured = IsConfigured(Profile(options, stage))
            })
        });
    }

    private static async Task<IResult> GetAgentsAsync(
        IReadOnlyRepository<AgentConfiguration> repository,
        IAgentPromptPlaceholderCatalog placeholders,
        CancellationToken ct)
    {
        var configuration = await repository.GetRequiredByIdAsync(AgentConfiguration.DocumentId, ct);
        return Results.Ok(new
        {
            stages = Enum.GetValues<AgentStage>().Select(stage => new
            {
                stage = stage.ToString(),
                configuration.GetStage(stage).Prompt,
                configuration.GetStage(stage).Model,
                configuration.GetStage(stage).ReasoningEffort,
                allowedPlaceholders = placeholders.GetForStage(stage)
            })
        });
    }

    private static async Task<IResult> GetModelsAsync(string stage, IAgentModelCatalog catalog, CancellationToken ct)
    {
        if (!TryParseStage(stage, out var parsed))
        {
            return Results.BadRequest(new { error = "Unknown agent stage." });
        }

        try
        {
            return Results.Ok(await catalog.GetModelsAsync(parsed, ct));
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            return Results.Problem("Gateway model discovery failed. Check this stage's endpoint and credential.", statusCode: 502);
        }
    }

    private static async Task<IResult> SaveAgentAsync(
        string stage,
        SetupAgentRequest request,
        IReadOnlyRepository<AgentConfiguration> repository,
        ISender sender,
        CancellationToken ct)
    {
        if (!TryParseStage(stage, out var parsed))
        {
            return Results.BadRequest(new { error = "Unknown agent stage." });
        }

        var configuration = await repository.GetRequiredByIdAsync(AgentConfiguration.DocumentId, ct);
        var result = await sender.Send(new SaveAgentStageConfigurationUsecase(
            parsed,
            request.Prompt ?? configuration.GetStage(parsed).Prompt,
            request.Model,
            request.ReasoningEffort), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetInputsAsync(
        IReadOnlyRepository<EmailAccount> accounts,
        IReadOnlyRepository<EmailInputConfiguration> email,
        IReadOnlyRepository<TodoistInputConfiguration> todoist,
        IReadOnlyRepository<TeamsInputConfiguration> teams,
        CancellationToken ct)
    {
        var emailConfiguration = await email.GetByIdAsync(EmailInputConfiguration.DocumentId, ct);
        var todoistConfiguration = await todoist.GetByIdAsync(TodoistInputConfiguration.DocumentId, ct);
        var teamsConfiguration = await teams.GetByIdAsync(TeamsInputConfiguration.DocumentId, ct);
        var emailAccounts = await accounts.ListAsync(new EmailAccountsSpecification(), ct);
        return Results.Ok(new
        {
            email = new
            {
                emailConfiguration?.PollingIntervalMinutes,
                emailConfiguration?.IgnoredSenderAddresses,
                accounts = emailAccounts.Select(account => new
                {
                    account.Id, account.DisplayName, account.EmailAddress, account.Host, account.Port,
                    security = account.Security.ToString(),
                    authenticationMethod = account.AuthenticationMethod.ToString(),
                    account.Username, account.IntakeGuidance, account.IsEnabled,
                    credentialConfigured = !string.IsNullOrWhiteSpace(account.ProtectedPassword)
                        || !string.IsNullOrWhiteSpace(account.ProtectedOAuthTokenCache),
                    account.LastSuccessfulCheckAt
                })
            },
            todoist = new
            {
                todoistConfiguration?.IsConfigured,
                todoistConfiguration?.PollingIntervalMinutes,
                todoistConfiguration?.LeadHours,
                todoistConfiguration?.OverdueDays,
                todoistConfiguration?.IntakeGuidance,
                todoistConfiguration?.IgnoredProjects,
                todoistConfiguration?.IgnoredLabels,
                todoistConfiguration?.LastSuccessfulCheckAt
            },
            teams = new
            {
                teamsConfiguration?.IsConfigured,
                tenantId = teamsConfiguration?.MicrosoftGraph.TenantId,
                clientId = teamsConfiguration?.MicrosoftGraph.ClientId,
                clientSecretConfigured = !string.IsNullOrWhiteSpace(teamsConfiguration?.MicrosoftGraph.ProtectedClientSecret),
                connected = teamsConfiguration?.ConnectedAccount is not null,
                teamsConfiguration?.PollingIntervalMinutes,
                teamsConfiguration?.DebounceMinutes,
                teamsConfiguration?.IntakeGuidance,
                teamsConfiguration?.IncludeChatMessages,
                teamsConfiguration?.IncludeChannelMessages,
                teamsConfiguration?.IncludeCallTranscripts,
                teamsConfiguration?.IgnoredPeople,
                teamsConfiguration?.IgnoredChats,
                teamsConfiguration?.IgnoredChannels,
                teamsConfiguration?.LastSuccessfulCheckAt
            }
        });
    }

    private static bool TryParseStage(string value, out AgentStage stage) =>
        Enum.TryParse(value, true, out stage) && Enum.IsDefined(stage);

    private static async Task<IResult> GetInputEventsAsync(
        int? limit, IReadOnlyRepository<InboundEvent> repository, CancellationToken ct)
    {
        var events = await repository.ListAsync(new RecentInboxEventsSpecification(Math.Clamp(limit ?? 10, 1, 100)), ct);
        return Results.Ok(new
        {
            inputEvents = events.Select(item => new
            {
                item.Id, item.Source, item.ExternalId, item.ReceivedAt, item.HandledAt,
                status = item.Status.ToString(), item.Explanation,
                outcomes = item.Outcomes.Select(outcome => new
                {
                    kind = outcome.Kind.ToString(), outcome.WorkItemId, outcome.Explanation
                })
            })
        });
    }

    private static async Task<IResult> GetWorkItemsAsync(
        int? limit, IReadOnlyRepository<WorkItem> repository, CancellationToken ct)
    {
        var items = await repository.ListAsync(new RecentWorkItemsSpecification(null, Math.Clamp(limit ?? 10, 1, 100)), ct);
        return Results.Ok(new
        {
            workItems = items.Select(item => new
            {
                item.Id, item.Title, status = item.Status.ToString(), item.Revision,
                item.CurrentPlanId, item.AgentTaskId, item.UpdatedAt,
                pendingQuestion = item.PendingPlanningQuestion?.Prompt,
                plans = item.Plans
            })
        });
    }

    private static bool IsConfigured(AgentEndpointProfileOptions profile) =>
        Uri.TryCreate(profile.BaseAddress, UriKind.Absolute, out _)
        && !string.IsNullOrWhiteSpace(profile.ApiKey);

    private static AgentEndpointProfileOptions Profile(AgentEndpointOptions options, AgentStage stage) => stage switch
    {
        AgentStage.Intake => options.Intake,
        AgentStage.Planning => options.Planning,
        AgentStage.Execution => options.Execution,
        AgentStage.AutomaticApproval => options.AutomaticApproval,
        AgentStage.AutoApprovedExecution => options.AutoApprovedExecution,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
}
