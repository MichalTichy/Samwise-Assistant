using Samwise.Domain.Agents;
using Samwise.Logic.Agents;
using Samwise.Logic.Prompts;
using Samwise.Logic.Prompts.Initialization;
using Samwise.Logic.Prompts.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Tests.Prompts;

public sealed class AgentConfigurationTests
{
    private static readonly DateTimeOffset InitialTime =
        new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DefaultConfigurationContainsEveryFixedStage()
    {
        var configuration = DefaultAgentConfiguration.Create(InitialTime);

        Assert.Equal(AgentConfiguration.DocumentId, configuration.Id);
        Assert.Empty(configuration.Intake.ReasoningEffort);
        Assert.Empty(configuration.Planning.ReasoningEffort);
        Assert.Empty(configuration.Execution.ReasoningEffort);
        Assert.Contains("{{input.text}}", configuration.Intake.Prompt);
        Assert.Contains("{{playbooks}}", configuration.Planning.Prompt);
        Assert.Contains("{{approved_plan}}", configuration.Execution.Prompt);
    }

    [Fact]
    public async Task SavingAStageUpdatesOnlyThatStageInTheExistingDocument()
    {
        var configuration = DefaultAgentConfiguration.Create(InitialTime);
        var repository = new AgentConfigurationRepository(configuration);
        var updatedAt = InitialTime.AddHours(1);
        var handler = new SaveAgentStageConfigurationUsecaseHandler(
            repository,
            new StaticAgentModelCatalog(),
            new PromptRenderer(),
            new FixedTimeProvider(updatedAt));

        var result = await handler.Handle(
            new SaveAgentStageConfigurationUsecase(
                AgentStage.Planning,
                " Plan {{work_item.title}} with {{playbooks}} ",
                " gpt-5.6-sol ",
                " max "),
            CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.Equal(0, repository.AddCount);
        Assert.Equal(1, repository.UpdateCount);
        Assert.Equal("Plan {{work_item.title}} with {{playbooks}}", configuration.Planning.Prompt);
        Assert.Equal("gpt-5.6-sol", configuration.Planning.Model);
        Assert.Equal("max", configuration.Planning.ReasoningEffort);
        Assert.Equal(updatedAt, configuration.Planning.UpdatedAt);
        Assert.Equal(InitialTime, configuration.Intake.UpdatedAt);
        Assert.Equal(InitialTime, configuration.Execution.UpdatedAt);
    }

    [Fact]
    public async Task SavingAStageDoesNotCreateAMissingConfigurationDocument()
    {
        var repository = new AgentConfigurationRepository();
        var handler = new SaveAgentStageConfigurationUsecaseHandler(
            repository,
            new StaticAgentModelCatalog(),
            new PromptRenderer(),
            new FixedTimeProvider(InitialTime));

        await Assert.ThrowsAsync<Exception>(() => handler.Handle(
            new SaveAgentStageConfigurationUsecase(
                AgentStage.Intake,
                "Classify {{input.text}} with {{active_work_items}} at {{current_time}}",
                "gpt-5.6-sol",
                "medium"),
            CancellationToken.None));

        Assert.Equal(0, repository.AddCount);
        Assert.Equal(0, repository.UpdateCount);
    }

    [Fact]
    public async Task SavingRejectsAnEffortNotAdvertisedByTheSelectedModel()
    {
        var repository = new AgentConfigurationRepository(DefaultAgentConfiguration.Create(InitialTime));
        var handler = new SaveAgentStageConfigurationUsecaseHandler(
            repository,
            new StaticAgentModelCatalog(
                new AgentModelDefinition("fixed-effort-model", ["high"], "high")),
            new PromptRenderer(),
            new FixedTimeProvider(InitialTime));

        var result = await handler.Handle(
            new SaveAgentStageConfigurationUsecase(
                AgentStage.Execution,
                "Execute {{approved_plan}} with {{execution_tools}} at {{current_time}} for {{work_item.id}} revision {{work_item.revision}} titled {{work_item.title}} using {{work_item.description}} and {{work_item.comments}}",
                "fixed-effort-model",
                "max"),
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Equal(
            "Reasoning effort 'max' is not supported by model 'fixed-effort-model'.",
            Assert.Single(result.Errors));
        Assert.Equal(0, repository.UpdateCount);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StaticAgentModelCatalog(params AgentModelDefinition[] models) : IAgentModelCatalog
    {
        private readonly IReadOnlyList<AgentModelDefinition> models = models.Length > 0
            ? models
            : [new AgentModelDefinition(
                "gpt-5.6-sol",
                ["none", "low", "medium", "high", "xhigh", "max"],
                "medium")];

        public Task<IReadOnlyList<AgentModelDefinition>> GetModelsAsync(
            AgentStage stage,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(models);
    }

    private sealed class AgentConfigurationRepository(AgentConfiguration? configuration = null)
        : IRepository<AgentConfiguration>
    {
        private AgentConfiguration? configuration = configuration;

        public int AddCount { get; private set; }
        public int UpdateCount { get; private set; }

        public Task<AgentConfiguration?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(configuration?.Id == id ? configuration : null);

        public Task<AgentConfiguration> AddAsync(
            AgentConfiguration entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null)
        {
            AddCount++;
            configuration = entity;
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(
            AgentConfiguration entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null)
        {
            UpdateCount++;
            configuration = entity;
            return Task.CompletedTask;
        }

        public Task AddAsync(ICollection<AgentConfiguration> entities, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentConfiguration>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<AgentConfiguration?> GetBySpecAsync(ISpecification<AgentConfiguration> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<AgentConfiguration, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentConfiguration>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AgentConfiguration>> ListAsync(IListSpecification<AgentConfiguration> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<AgentConfiguration, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task GetAndUpdateAsync(string id, Func<AgentConfiguration, Task> updateMethod, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteAsync(AgentConfiguration entity, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteRangeByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();
    }
}
