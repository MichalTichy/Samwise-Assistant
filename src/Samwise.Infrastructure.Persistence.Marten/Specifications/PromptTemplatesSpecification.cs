using Marten;
using Samwise.Domain.Prompts;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Infrastructure.Persistence.Marten.Specifications;

internal sealed class PromptTemplatesSpecification : IListSpecification<PromptTemplate>
{
    public async Task<IReadOnlyList<PromptTemplate>> ApplyAsync(
        IQueryable<PromptTemplate> queryable,
        CancellationToken cancellationToken = default) =>
        await queryable
            .OrderBy(template => template.Stage)
            .ToListAsync(cancellationToken);
}
