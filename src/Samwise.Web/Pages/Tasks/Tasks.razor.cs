using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Samwise.Domain.Tasks;
using Samwise.Logic.Tasks;
using Samwise.Logic.Tasks.Usecases;
using Samwise.Web.Resources;
using Shared.Infrastructure.Persistence.Repositories;

namespace Samwise.Web.Pages.Tasks;

public partial class Tasks : IAsyncDisposable
{
    [Inject] private ISender Sender { get; set; } = default!;
    [Inject] private IReadOnlyRepository<AgentTask> TaskRepository { get; set; } = default!;
    [Inject] private IReadOnlyRepository<AgentRoutine> RoutineRepository { get; set; } = default!;
    [Inject] private ILogger<Tasks> Logger { get; set; } = default!;
    [Inject] private IJSRuntime Js { get; set; } = default!;
    [SupplyParameterFromQuery] public string? TaskId { get; set; }

    private IReadOnlyList<TaskSummary> tasks = [];
    private IReadOnlyList<RoutineSummary> routines = [];
    private List<TaskChecklistItem> checklist = [new()];
    private List<string> workItemIds = [];
    private string? selectedId;
    private int version;
    private string title = string.Empty, instructions = string.Empty, timeZoneId = "Europe/Prague";
    private string repeat = "daily", originalCron = "0 9 * * *";
    private TimeOnly runTime = new(9, 0);
    private int weekDay = 1, monthDay = 1;
    private DateTime? nextRunLocal;
    private bool isRoutine, isEnabled = true, includeCompleted = true, busy, editorOpen;
    private string? selectedRoutineId;
    private ExternalTaskReference? externalSource;
    private string SourceDueDisplay => externalSource?.DueAt is { } due
        ? TimeZoneInfo.ConvertTimeBySystemTimeZoneId(due, "Europe/Prague").ToString("g") + " (Europe/Prague)" : "—";
    private DateTime selectedDate = LocalToday;
    private DateTime viewMonth = new(LocalToday.Year, LocalToday.Month, 1);
    private List<TaskCalendarEntry> calendarEntries = [];
    private static DateTime LocalToday => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "Europe/Prague").Date;
    private DateTime GridStart => viewMonth.AddDays(-((int)viewMonth.DayOfWeek + 6) % 7);
    private string? error, message;
    private ElementReference dialogElement;
    private IJSObjectReference? dialogModule;
    private DotNetObjectReference<Tasks>? dialogReference;
    private bool dialogWasOpen;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await LoadAsync();
            if (TaskId is not null) await SelectTaskAsync(TaskId);
            StateHasChanged();
            return;
        }
        if (editorOpen && !dialogWasOpen)
        {
            dialogModule ??= await Js.InvokeAsync<IJSObjectReference>("import", "./Pages/Tasks/Tasks.razor.js");
            dialogReference ??= DotNetObjectReference.Create(this);
            await dialogModule.InvokeVoidAsync("open", dialogElement, dialogReference);
        }
        dialogWasOpen = editorOpen;
    }

    private async Task LoadAsync()
    {
        busy = true;
        error = null;
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
            var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(GridStart, DateTimeKind.Unspecified), zone));
            tasks = await TaskRepository.ListAsync(new TaskSummariesSpecification(true, start, start.AddDays(43)));
            routines = await RoutineRepository.ListAsync(new RoutineSummariesSpecification());
            BuildCalendar();
        }
        catch (Exception exception) { Logger.LogError(exception, "Loading agent tasks failed"); error = TaskResources.LoadError; }
        finally { busy = false; }
    }

    private void New(bool routine)
    {
        isRoutine = routine;
        editorOpen = true;
        selectedRoutineId = null;
        externalSource = null;
        selectedId = null;
        version = 0;
        title = instructions = string.Empty;
        checklist = [];
        workItemIds = [];
        nextRunLocal = selectedDate > LocalToday ? selectedDate.AddHours(9) :
            TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow.AddMinutes(15), "Europe/Prague").DateTime;
        timeZoneId = "Europe/Prague";
        runTime = new TimeOnly(9, 0);
        repeat = "daily";
        isEnabled = true;
        error = message = null;
    }

    private async Task SelectTaskAsync(string id)
    {
        busy = true;
        try
        {
            var task = await TaskRepository.GetRequiredByIdAsync(id);
            New(false);
            selectedId = task.Id;
            version = task.Version;
            title = task.Title;
            instructions = task.Instructions;
            checklist = task.Checklist.Select(item => item.Copy()).ToList();
            workItemIds = task.WorkItemIds.ToList();
            selectedRoutineId = task.RoutineId;
            externalSource = task.ExternalSource;
            nextRunLocal = task.NextRunAt is { } next ? TimeZoneInfo.ConvertTime(next, TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime : null;
        }
        catch (Exception exception) { Logger.LogError(exception, "Loading task failed"); error = TaskResources.LoadError; }
        finally { busy = false; }
    }

    private async Task SelectRoutineAsync(string id)
    {
        busy = true;
        try
        {
            var routine = await RoutineRepository.GetRequiredByIdAsync(id);
            New(true);
            selectedId = routine.Id;
            version = routine.Version;
            title = routine.Title;
            instructions = routine.Instructions;
            checklist = routine.Checklist.Select(item => item.Copy(reset: true)).ToList();
            timeZoneId = routine.TimeZoneId;
            isEnabled = routine.IsEnabled;
            originalCron = routine.Cron;
            var parts = routine.Cron.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            repeat = "custom";
            if (parts.Length == 5 && int.TryParse(parts[0], out var minute) && int.TryParse(parts[1], out var hour))
            {
                runTime = new TimeOnly(hour, minute);
                var tail = string.Join(' ', parts.Skip(2));
                repeat = tail switch
                {
                    "* * *" => "daily", "* * 1-5" => "weekdays", "1W * *" => "first",
                    "LW * *" => "last", "* * 1#2" => "secondMonday", _ => "custom"
                };
                if (parts[2] == "*" && parts[3] == "*" && int.TryParse(parts[4], out var day) && day is >= 0 and <= 6)
                { weekDay = day; repeat = "weekly"; }
                if (int.TryParse(parts[2], out var date) && parts[3] == "*" && parts[4] == "*")
                { monthDay = date; repeat = "monthly"; }
            }
        }
        catch (Exception exception) { Logger.LogError(exception, "Loading recurrence failed"); error = TaskResources.LoadError; }
        finally { busy = false; }
    }

    private void AddStep() => checklist.Add(new TaskChecklistItem());

    private async Task SaveAsync()
    {
        if (busy) return;
        busy = true;
        error = message = null;
        try
        {
            if (string.IsNullOrWhiteSpace(instructions)) instructions = title;
            if (checklist.Count == 0) checklist = [new() { Text = title }];
            if (string.IsNullOrWhiteSpace(title) ||
                checklist.Any(item => string.IsNullOrWhiteSpace(item.Text)))
            { error = TaskResources.Required; return; }
            if (isRoutine)
            {
                var tail = repeat switch
                {
                    "daily" => "* * *", "weekdays" => "* * 1-5", "weekly" => $"* * {weekDay}",
                    "monthly" => $"{monthDay} * *", "first" => "1W * *", "last" => "LW * *", "secondMonday" => "* * 1#2", _ => null
                };
                var cron = tail is null ? originalCron : $"{runTime.Minute} {runTime.Hour} {tail}";
                var saved = await Sender.Send(new SaveAgentRoutineUsecase(selectedId, version, title, instructions,
                    checklist, cron, timeZoneId, isEnabled));
                await SelectRoutineAsync(saved.Id);
            }
            else
            {
                DateTimeOffset? next = null;
                if (nextRunLocal is null && checklist.Any(item => !item.IsCompleted))
                { error = TaskResources.DateRequired; return; }
                if (nextRunLocal is { } local && checklist.Any(item => !item.IsCompleted))
                {
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
                    if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
                    { error = TaskResources.InvalidTime; return; }
                    next = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
                }
                foreach (var step in checklist.Where(item => item.IsCompleted && string.IsNullOrWhiteSpace(item.Evidence)))
                    step.Evidence = TaskResources.ManualEvidence;
                var saved = await Sender.Send(new SaveAgentTaskUsecase(selectedId, version, title, instructions, checklist, next, IsOwnerEdit: true));
                await SelectTaskAsync(saved.Id);
            }
            await LoadAsync();
            editorOpen = false;
            message = TaskResources.Saved;
        }
        catch (Exception exception) { Logger.LogError(exception, "Saving agent task failed"); error = TaskResources.SaveError; }
        finally { busy = false; }
    }

    private async Task ChangeMonth(int delta) { viewMonth = viewMonth.AddMonths(delta); await LoadAsync(); }
    private async Task Today() { selectedDate = LocalToday; viewMonth = new(selectedDate.Year, selectedDate.Month, 1); await LoadAsync(); }
    private IEnumerable<TaskCalendarEntry> EntriesOn(DateTime day) => calendarEntries
        .Where(entry => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(entry.At, "Europe/Prague").Date == day.Date)
        .Where(entry => includeCompleted || !entry.IsCompleted).OrderBy(entry => entry.At);
    private static string EntryTime(TaskCalendarEntry entry) =>
        TimeZoneInfo.ConvertTimeBySystemTimeZoneId(entry.At, "Europe/Prague").ToString("HH:mm");

    private void BuildCalendar()
    {
        var enabledRoutines = routines.Where(routine => routine.IsEnabled).Select(routine => routine.Id).ToHashSet(StringComparer.Ordinal);
        calendarEntries = tasks.Where(task => task.RoutineId is null || enabledRoutines.Contains(task.RoutineId) ||
                task.LastRunAt is not null || task.CompletedAt is not null)
            .Where(task => task.NextRunAt is not null || task.LastRunAt is not null || task.OccurrenceAt is not null)
            .Select(task => new TaskCalendarEntry(task.Id, task.Title, task.NextRunAt ?? task.LastRunAt ?? task.OccurrenceAt!.Value,
                task.RoutineId, false, task.CompletedAt is not null, task.CompletedCount, task.ItemCount, task.SourceState)).ToList();
        var existing = tasks.Where(task => task.RoutineId is not null && task.OccurrenceAt is not null)
            .Select(task => $"{task.RoutineId}:{task.OccurrenceAt!.Value.UtcTicks}").ToHashSet(StringComparer.Ordinal);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Prague");
        var start = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(GridStart, DateTimeKind.Unspecified), zone));
        var end = start.AddDays(43);
        foreach (var routine in routines.Where(item => item.IsEnabled && item.NextOccurrenceAt is not null))
        {
            var after = routine.NextOccurrenceAt!.Value > start ? routine.NextOccurrenceAt.Value.AddTicks(-1) : start.AddTicks(-1);
            for (var i = 0; i < 200; i++)
            {
                var at = TaskRecurrence.Next(routine.Cron, routine.TimeZoneId, after);
                if (at >= end) break;
                var id = $"{routine.Id}:{at.UtcTicks}";
                if (!existing.Contains(id)) calendarEntries.Add(new TaskCalendarEntry(id, routine.Title, at, routine.Id, true, false, 0, 0));
                after = at;
            }
        }
    }

    private async Task SelectEntryAsync(TaskCalendarEntry entry)
    {
        try
        {
            if (entry.IsProjection)
            {
                await Sender.Send(new MaterializeTaskOccurrenceUsecase(entry.RoutineId!, entry.At));
                await LoadAsync();
            }
            await SelectTaskAsync(entry.Id);
        }
        catch (Exception exception) { Logger.LogError(exception, "Opening task occurrence failed"); error = TaskResources.LoadError; }
    }

    private void SetRepeat(ChangeEventArgs args)
    {
        repeat = args.Value?.ToString() ?? "none";
        isRoutine = repeat != "none";
    }

    [JSInvokable]
    public Task DismissDialog()
    {
        editorOpen = false;
        dialogWasOpen = false;
        StateHasChanged();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        dialogReference?.Dispose();
        if (dialogModule is null) return;
        try { await dialogModule.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
