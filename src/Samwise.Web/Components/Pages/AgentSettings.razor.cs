using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Agents;
using Samwise.Logic.Agents;
using Samwise.Logic.Prompts;
using Samwise.Logic.Prompts.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Components.Pages;

public partial class AgentSettings
{
    private IReadOnlyList<AgentStageConfigurationEditor> configurations = [];
    private AgentStageConfigurationEditor? selected;
    private string content = string.Empty;
    private string model = string.Empty;
    private string reasoningEffort = string.Empty;
    private IReadOnlyDictionary<AgentStage, IReadOnlyList<AgentModelDefinition>> modelsByStage =
        new Dictionary<AgentStage, IReadOnlyList<AgentModelDefinition>>();
    private IReadOnlyDictionary<AgentStage, ToolCatalogSnapshot> toolsByStage =
        new Dictionary<AgentStage, ToolCatalogSnapshot>();
    private IReadOnlyDictionary<AgentStage, string> toolErrorsByStage =
        new Dictionary<AgentStage, string>();
    private IReadOnlyList<AgentModelDefinition> availableModels = [];
    private IReadOnlyList<string> availableReasoningEfforts = [];
    private IReadOnlyList<string> errors = [];
    private string? modelCatalogError;
    private string? saveMessage;
    private bool saving;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<AgentConfiguration> AgentConfigurationRepository { get; set; } = default!;

    [Inject]
    private IPromptRenderer PromptRenderer { get; set; } = default!;

    [Inject]
    private IAgentModelCatalog ModelCatalog { get; set; } = default!;

    [Inject]
    private IAgentToolCatalog ToolCatalog { get; set; } = default!;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            await LoadModelCatalogsAsync();
        }
        catch (Exception exception)
        {
            modelCatalogError = $"The Gateway model catalog could not be loaded: {exception.Message}";
        }

        await LoadToolCatalogsAsync();

        await LoadConfigurationsAsync();
        Select(configurations.FirstOrDefault(configuration => configuration.Stage == AgentStage.Planning) ?? configurations.FirstOrDefault());
    }

    private void Select(AgentStageConfigurationEditor? configuration)
    {
        selected = configuration;
        content = configuration?.Prompt ?? string.Empty;
        model = configuration?.Model ?? string.Empty;
        reasoningEffort = configuration?.ReasoningEffort ?? string.Empty;
        availableModels = configuration is null
            ? []
            : modelsByStage.GetValueOrDefault(configuration.Stage, []);
        SelectReasoningEfforts(useModelDefaultWhenInvalid: string.IsNullOrWhiteSpace(model));
        errors = [];
        saveMessage = null;
    }

    private void ModelChanged(ChangeEventArgs args)
    {
        model = args.Value?.ToString() ?? string.Empty;
        SelectReasoningEfforts(useModelDefaultWhenInvalid: true);
        errors = [];
        saveMessage = null;
    }

    private void SelectReasoningEfforts(bool useModelDefaultWhenInvalid)
    {
        var selectedModel = availableModels.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, model, StringComparison.Ordinal));
        if (selectedModel is null && string.IsNullOrWhiteSpace(model) && availableModels.Count > 0)
        {
            selectedModel = availableModels[0];
            model = selectedModel.Id;
        }

        availableReasoningEfforts = selectedModel?.SupportedReasoningEfforts ?? [];
        if (selectedModel is not null &&
            (useModelDefaultWhenInvalid ||
             !availableReasoningEfforts.Contains(reasoningEffort, StringComparer.Ordinal)))
        {
            reasoningEffort = selectedModel.DefaultReasoningEffort;
        }
    }

    private async Task LoadModelCatalogsAsync()
    {
        var catalogs = await Task.WhenAll(Enum.GetValues<AgentStage>().Select(async stage =>
            new
            {
                Stage = stage,
                Models = await ModelCatalog.GetModelsAsync(stage)
            }));
        modelsByStage = catalogs.ToDictionary(
            catalog => catalog.Stage,
            catalog => catalog.Models);
        modelCatalogError = null;
    }

    private async Task LoadToolCatalogsAsync()
    {
        var results = await Task.WhenAll(Enum.GetValues<AgentStage>().Select(async stage =>
        {
            try
            {
                return new ToolCatalogLoadResult(
                    stage,
                    await ToolCatalog.GetToolsAsync(stage),
                    null);
            }
            catch (Exception exception)
            {
                return new ToolCatalogLoadResult(stage, null, exception.Message);
            }
        }));

        toolsByStage = results
            .Where(result => result.Catalog is not null)
            .ToDictionary(
                result => result.Stage,
                result => result.Catalog!);
        toolErrorsByStage = results
            .Where(result => result.Error is not null)
            .ToDictionary(
                result => result.Stage,
                result => $"The Gateway MCP catalog could not be loaded: {result.Error}");
    }

    private async Task SaveAsync()
    {
        if (selected is null)
        {
            return;
        }

        saving = true;
        saveMessage = null;
        try
        {
            var selectedStage = selected.Stage;
            var result = await Sender.Send(new SaveAgentStageConfigurationUsecase(
                selectedStage,
                content,
                model,
                reasoningEffort));
            errors = result.Errors;
            saveMessage = result.Succeeded ? "Saved" : null;
            if (result.Succeeded)
            {
                await LoadModelCatalogsAsync();
                await LoadConfigurationsAsync();
                Select(configurations.Single(configuration => configuration.Stage == selectedStage));
                saveMessage = "Saved";
            }
        }
        catch (Exception exception)
        {
            errors = [$"Saving agent settings failed: {exception.Message}"];
        }
        finally
        {
            saving = false;
        }
    }

    private async Task LoadConfigurationsAsync()
    {
        var configuration = await AgentConfigurationRepository.GetRequiredByIdAsync(AgentConfiguration.DocumentId);
        configurations = Enum.GetValues<AgentStage>()
            .Select(stage =>
            {
                var stageConfiguration = configuration.GetStage(stage);
                return new AgentStageConfigurationEditor(
                    stage,
                    stage.ToString(),
                    stageConfiguration.Prompt,
                    stageConfiguration.Model,
                    stageConfiguration.ReasoningEffort,
                    stageConfiguration.UpdatedAt,
                    PromptRenderer.GetPlaceholders(stage));
            })
            .ToList();
    }

    private static string DescribeStage(AgentStage stage) => stage switch
    {
        AgentStage.Intake => "Classify every input",
        AgentStage.Planning => "Create approval plan",
        AgentStage.Execution => "Run approved plan",
        _ => stage.ToString()
    };

    private static string FormatEffort(string effort) => effort switch
    {
        "xhigh" => "Extra high",
        _ when effort.Length > 0 => char.ToUpperInvariant(effort[0]) + effort[1..],
        _ => string.Empty
    };

    private ToolCatalogSnapshot? SelectedToolCatalog => selected is null
        ? null
        : toolsByStage.GetValueOrDefault(selected.Stage);

    private string? SelectedToolCatalogError => selected is null
        ? null
        : toolErrorsByStage.GetValueOrDefault(selected.Stage);

    private static string ServerTitle(ToolServerCapability server) =>
        string.IsNullOrWhiteSpace(server.Title) ? server.Name : server.Title;

    private static string FormatCatalogVersion(string version) => version.Length <= 23
        ? version
        : string.Concat(version.AsSpan(0, 15), "…", version.AsSpan(version.Length - 7));

    private sealed record ToolCatalogLoadResult(
        AgentStage Stage,
        ToolCatalogSnapshot? Catalog,
        string? Error);
}
