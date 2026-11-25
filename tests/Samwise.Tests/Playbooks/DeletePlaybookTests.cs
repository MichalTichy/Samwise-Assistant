using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Samwise.Domain.Playbooks;
using Samwise.Logic.Playbooks.Usecases;
using Shared.Infrastructure.Persistence.Repositories;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Tests.Playbooks;

public sealed class DeletePlaybookTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeletesEnabledOrDisabledPlaybookAndRepeatedDeletionIsHarmless(bool enabled)
    {
        var repository = new PlaybookRepository(new Playbook { Id = "target", IsEnabled = enabled });
        using var services = CreateServices(repository);
        var sender = services.GetRequiredService<ISender>();

        Assert.False(await sender.Send(new DeletePlaybookUsecase("other")));
        Assert.Equal(0, repository.DeleteCount);
        Assert.True(await sender.Send(new DeletePlaybookUsecase("target")));
        Assert.Null(await repository.GetByIdAsync("target"));
        Assert.False(await sender.Send(new DeletePlaybookUsecase("target")));
        Assert.Equal(1, repository.DeleteCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task EmptyIdDoesNotDeleteAnything(string id)
    {
        var repository = new PlaybookRepository(new Playbook { Id = "target" });
        using var services = CreateServices(repository);

        Assert.False(await services.GetRequiredService<ISender>().Send(new DeletePlaybookUsecase(id)));
        Assert.Equal(0, repository.DeleteCount);
        Assert.NotNull(await repository.GetByIdAsync("target"));
    }

    private static ServiceProvider CreateServices(PlaybookRepository repository) =>
        new ServiceCollection()
            .AddSingleton<IRepository<Playbook>>(repository)
            .AddLogging()
            .AddMediatR(options => options.RegisterServicesFromAssembly(typeof(DeletePlaybookUsecase).Assembly))
            .BuildServiceProvider();

    private sealed class PlaybookRepository(Playbook? configuration = null)
        : IRepository<Playbook>
    {
        private Playbook? configuration = configuration;

        public int DeleteCount { get; private set; }

        public int AddCount { get; private set; }
        public int UpdateCount { get; private set; }

        public Task<Playbook?> GetByIdAsync(
            string id,
            CancellationToken cancellationToken = default,
            string? tenantId = null) =>
            Task.FromResult(configuration?.Id == id ? configuration : null);

        public Task<Playbook> AddAsync(
            Playbook entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null)
        {
            AddCount++;
            configuration = entity;
            return Task.FromResult(entity);
        }

        public Task UpdateAsync(
            Playbook entity,
            CancellationToken cancellationToken = default,
            string? tenantId = null)
        {
            UpdateCount++;
            configuration = entity;
            return Task.CompletedTask;
        }

        public Task AddAsync(ICollection<Playbook> entities, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Playbook>> GetByIdsAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<Playbook?> GetBySpecAsync(ISpecification<Playbook> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<TResult?> GetBySpecAsync<TResult>(ISpecification<Playbook, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Playbook>> ListAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Playbook>> ListAsync(IListSpecification<Playbook> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<TResult>> ListAsync<TResult>(IListSpecification<Playbook, TResult> specification, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task<int> CountAsync(CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task GetAndUpdateAsync(string id, Func<Playbook, Task> updateMethod, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Playbook entity, CancellationToken cancellationToken = default, string? tenantId = null)
        {
            DeleteCount++;
            configuration = null;
            return Task.CompletedTask;
        }

        public Task DeleteByIdAsync(string id, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();

        public Task DeleteRangeByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default, string? tenantId = null) =>
            throw new NotSupportedException();
    }
}
