namespace Samwise.Logic.Agents;

public enum PlanningDisposition
{
    PlanRequired,
    UserInputRequired,
    AlreadyResolved,
    NoLongerRelevant,
    AwaitingExternalEvent
}
