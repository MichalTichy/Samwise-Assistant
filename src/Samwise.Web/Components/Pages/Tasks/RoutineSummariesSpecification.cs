using Marten;
using Samwise.Domain.Tasks;
using Shared.Infrastructure.Persistence.Specifications;

namespace Samwise.Web.Components.Pages.Tasks;

internal sealed class RoutineSummariesSpecification : IListSpecification<AgentRoutine, RoutineSummary>
{
    public async Task<IReadOnlyList<RoutineSummary>> ApplyAsync(IQueryable<AgentRoutine> queryable,
        CancellationToken cancellationToken = default) => await queryable.OrderBy(routine => routine.Title).Take(200)
        .Select(routine => new RoutineSummary(routine.Id, routine.Title, routine.IsEnabled, routine.NextOccurrenceAt, routine.Cron, routine.TimeZoneId))
        .ToListAsync(cancellationToken);
}
