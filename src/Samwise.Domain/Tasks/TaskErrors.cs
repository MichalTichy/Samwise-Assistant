using System.Globalization;
using System.Resources;

namespace Samwise.Domain.Tasks;

public static class TaskErrors
{
    public static string ChangedSource => Manager.GetString(nameof(ChangedSource), CultureInfo.CurrentUICulture)!;
    public static string InterruptedExecution => Manager.GetString(nameof(InterruptedExecution), CultureInfo.CurrentUICulture)!;
    public static string SourceUnavailable => Manager.GetString(nameof(SourceUnavailable), CultureInfo.CurrentUICulture)!;
    public static string ExternalSourceOwned => Manager.GetString(nameof(ExternalSourceOwned), CultureInfo.CurrentUICulture)!;
    private static readonly ResourceManager Manager = new(typeof(TaskErrors));
    public static string InvalidTask => Manager.GetString(nameof(InvalidTask), CultureInfo.CurrentUICulture)!;
    public static string CompletedWithNext => Manager.GetString(nameof(CompletedWithNext), CultureInfo.CurrentUICulture)!;
    public static string NotDue => Manager.GetString(nameof(NotDue), CultureInfo.CurrentUICulture)!;
    public static string NoOccurrence => Manager.GetString(nameof(NoOccurrence), CultureInfo.CurrentUICulture)!;
    public static string StaleTask => Manager.GetString(nameof(StaleTask), CultureInfo.CurrentUICulture)!;
    public static string FutureCheck => Manager.GetString(nameof(FutureCheck), CultureInfo.CurrentUICulture)!;
    public static string ResolveExecution => Manager.GetString(nameof(ResolveExecution), CultureInfo.CurrentUICulture)!;
    public static string MissingNext => Manager.GetString(nameof(MissingNext), CultureInfo.CurrentUICulture)!;
    public static string StaleRoutine => Manager.GetString(nameof(StaleRoutine), CultureInfo.CurrentUICulture)!;
    public static string InvalidRecurrence => Manager.GetString(nameof(InvalidRecurrence), CultureInfo.CurrentUICulture)!;
    public static string InvalidOccurrence => Manager.GetString(nameof(InvalidOccurrence), CultureInfo.CurrentUICulture)!;
    public static string RetiredPlan => Manager.GetString(nameof(RetiredPlan), CultureInfo.CurrentUICulture)!;
    public static string MissingProgress => Manager.GetString(nameof(MissingProgress), CultureInfo.CurrentUICulture)!;
    public static string MissingPlanningProgress => Manager.GetString(nameof(MissingPlanningProgress), CultureInfo.CurrentUICulture)!;
    public static string LegacyWaiting => Manager.GetString(nameof(LegacyWaiting), CultureInfo.CurrentUICulture)!;
}
