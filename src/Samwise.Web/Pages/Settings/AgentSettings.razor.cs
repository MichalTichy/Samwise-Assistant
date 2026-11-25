using Samwise.Web.Components;
using MediatR;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Agents;
using Samwise.Logic.Agents;
using Samwise.Logic.Prompts;
using Samwise.Logic.Prompts.Usecases;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Pages.Settings;

public partial class AgentSettings : AppComponentBase
{
    private IReadOnlyList<AgentStageConfigurationEditor> configurations = [];
    private AgentStageConfigurationEditor? selected;
    private string content = string.Empty;
    private string model = string.Empty;
    private string reasoningEffort = string.Empty;
    private readonly Dictionary<AgentStage, IReadOnlyList<AgentModelDefinition>> modelsByStage = [];
    private readonly Dictionary<AgentStage, ToolCatalogSnapshot> toolsByStage = [];
    private readonly Dictionary<AgentStage, string> modelErrorsByStage = [];
    private readonly Dictionary<AgentStage, string> toolErrorsByStage = [];
    private readonly HashSet<AgentStage> loadingModelStages = [];
    private readonly HashSet<AgentStage> loadingToolStages = [];
    private IReadOnlyList<AgentModelDefinition> availableModels = [];
    private IReadOnlyList<string> availableReasoningEfforts = [];
    private IReadOnlyList<string> errors = [];
    private string? saveMessage;
    private bool saving;

    [Inject]
    private ISender Sender { get; set; } = default!;

    [Inject]
    private IReadOnlyRepository<AgentConfiguration> AgentConfigurationRepository { get; set; } = default!;

    [Inject]
    private IAgentPromptPlaceholderCatalog PlaceholderCatalog { get; set; } = default!;

    [Inject]
    private IAgentModelCatalog ModelCatalog { get; set; } = default!;

    [Inject]
    private IAgentToolCatalog ToolCatalog { get; set; } = default!;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
        {
            return;
        }

        await LoadConfigurationsAsync();
        Select(configurations.FirstOrDefault(configuration => configuration.Stage == AgentStage.Planning)
            ?? configurations.FirstOrDefault());

        if (selected is not null)
        {
            await LoadStageCatalogsAsync(selected.Stage);
        }
    }

    private async Task SelectAsync(AgentStageConfigurationEditor configuration)
    {
        Select(configuration);
        await LoadStageCatalogsAsync(configuration.Stage);
    }

    private async Task LoadStageCatalogsAsync(AgentStage stage)
    {
        var modelsTask = !modelsByStage.ContainsKey(stage) &&
            !modelErrorsByStage.ContainsKey(stage) && loadingModelStages.Add(stage)
                ? LoadStageModelsAsync(stage)
                : Task.CompletedTask;
        var toolsTask = !toolsByStage.ContainsKey(stage) &&
            !toolErrorsByStage.ContainsKey(stage) && loadingToolStages.Add(stage)
                ? LoadStageToolsAsync(stage)
                : Task.CompletedTask;
        await Task.WhenAll(modelsTask, toolsTask);
    }

    private async Task LoadStageModelsAsync(AgentStage stage)
    {
        try
        {
            modelsByStage[stage] = await ModelCatalog.GetModelsAsync(stage, ComponentToken);
        }
        catch (Exception exception)
        {
            modelErrorsByStage[stage] = $"The Gateway model catalog could not be loaded: {exception.Message}";
        }
        finally
        {
            loadingModelStages.Remove(stage);
            if (selected?.Stage == stage)
            {
                availableModels = modelsByStage.GetValueOrDefault(stage, []);
                SelectReasoningEfforts(useModelDefaultWhenInvalid: string.IsNullOrWhiteSpace(model));
            }
            StateHasChanged();
        }
    }

    private async Task LoadStageToolsAsync(AgentStage stage)
    {
        try
        {
            toolsByStage[stage] = await ToolCatalog.GetToolsAsync(stage, ComponentToken);
        }
        catch (Exception exception)
        {
            toolErrorsByStage[stage] = $"The Gateway MCP catalog could not be loaded: {exception.Message}";
        }
        finally
        {
            loadingToolStages.Remove(stage);
            StateHasChanged();
        }
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

    private Task LoadConfigurationsAsync() => ExecuteLoadAsync(
        token => AgentConfigurationRepository.GetRequiredByIdAsync(AgentConfiguration.DocumentId, token),
        configuration => configurations = Enum.GetValues<AgentStage>()
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
                    PlaceholderCatalog.GetForStage(stage));
            })
            .ToList());

    private static string DescribeStage(AgentStage stage) => stage switch
    {
        AgentStage.Intake => "Classify every input",
        AgentStage.Planning => "Create approval plan",
        AgentStage.Execution => "Run approved plan",
        AgentStage.AutomaticApproval => "Review eligible plans without tools",
        AgentStage.AutoApprovedExecution => "Run automatically approved plans with restricted tools",
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

    private string? SelectedModelCatalogError => selected is null
        ? null
        : modelErrorsByStage.GetValueOrDefault(selected.Stage);

    private bool loadingModels => selected is not null &&
        !modelsByStage.ContainsKey(selected.Stage) &&
        !modelErrorsByStage.ContainsKey(selected.Stage);

    private bool loadingTools => selected is not null &&
        !toolsByStage.ContainsKey(selected.Stage) &&
        !toolErrorsByStage.ContainsKey(selected.Stage);

    private static string ServerTitle(ToolServerCapability server) =>
        string.IsNullOrWhiteSpace(server.Title) ? server.Name : server.Title;

    private static string FormatCatalogVersion(string version) => version.Length <= 23
        ? version
        : string.Concat(version.AsSpan(0, 15), "…", version.AsSpan(version.Length - 7));

}
