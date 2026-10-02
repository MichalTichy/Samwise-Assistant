using System.Globalization;
using Microsoft.AspNetCore.Components;
using Samwise.Logic.Notifications;
using Samwise.Logic.Notifications.Specifications;
using Shared.Infrastructure.Persistence.Repositories;
using DomainWorkItem = Samwise.Domain.WorkItems.WorkItem;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Web.Components.Pages;

public partial class AutoExecutionReport
{
    private IReadOnlyList<ReportDay> days = [];
    private string dateInput = string.Empty;
    private string? loadedDate;
    private string? error;
    private bool loading = true;

    [Inject]
    private IReadOnlyRepository<DomainWorkItem> WorkItemRepository { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    [Inject]
    private TimeProvider TimeProvider { get; set; } = default!;

    [SupplyParameterFromQuery(Name = "date")]
    public string? Date { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender || loadedDate != Date)
        {
            await LoadAsync();
            StateHasChanged();
        }
    }

    private async Task LoadAsync()
    {
        loading = true;
        error = null;
        loadedDate = Date;
        dateInput = Date ?? string.Empty;
        try
        {
            var today = AutoExecutionReportPeriod.LocalDate(TimeProvider.GetUtcNow());
            DateOnly firstDay;
            DateOnly lastDay;
            if (Date is null)
            {
                firstDay = today.AddDays(-29);
                lastDay = today;
            }
            else if (DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                         DateTimeStyles.None, out var requestedDay))
            {
                firstDay = lastDay = requestedDay;
            }
            else
            {
                error = "The report date must use the format YYYY-MM-DD.";
                days = [];
                return;
            }

            var workItems = await WorkItemRepository.ListAsync(new AutoExecutedWorkItemsSpecification(
                AutoExecutionReportPeriod.ToUtcMidnight(firstDay),
                AutoExecutionReportPeriod.ToUtcMidnight(lastDay.AddDays(1))));
            days = workItems
                .GroupBy(workItem => AutoExecutionReportPeriod.LocalDate(workItem.CompletedAt!.Value))
                .OrderByDescending(group => group.Key)
                .Select(group => new ReportDay(group.Key, group.ToList()))
                .ToList();
        }
        catch (Exception exception)
        {
            error = $"The auto-execution report could not be loaded: {exception.Message}";
            days = [];
        }
        finally
        {
            loading = false;
        }
    }

    private void ApplyDate() =>
        Navigation.NavigateTo(Navigation.GetUriWithQueryParameter(
            "date", string.IsNullOrWhiteSpace(dateInput) ? null : dateInput));

    private void OnDateChanged(ChangeEventArgs args) => dateInput = args.Value?.ToString() ?? string.Empty;

    private static string FormatCompletionTime(DomainWorkItem workItem) =>
        workItem.CompletedAt is { } completedAt
            ? AutoExecutionReportPeriod.LocalTime(completedAt).ToString("HH:mm", CultureInfo.CurrentCulture)
            : string.Empty;

    private sealed record ReportDay(DateOnly Date, IReadOnlyList<DomainWorkItem> WorkItems);
}
