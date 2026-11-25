using Samwise.Web.Components;
using Microsoft.AspNetCore.Components;
using Samwise.Domain.Agents;
using Samwise.Logic.WorkItems.Specifications;
using Shared.Infrastructure.Persistence.Repositories;
using Task = System.Threading.Tasks.Task;

namespace Samwise.Web.Pages.WorkItems;

public partial class WorkItemAgentActivity : AppComponentBase
{
    private IReadOnlyList<AgentInvocation> invocations = [];
    private AgentInvocation? selectedInvocation;
    private bool activityDetailsOpen;

    [Parameter, EditorRequired] public string WorkItemId { get; set; } = string.Empty;
    [Inject] private IReadOnlyRepository<AgentInvocation> InvocationRepository { get; set; } = default!;

    protected override Task OnAfterRenderAsync(bool firstRender) => firstRender
        ? ExecuteLoadAsync(token => InvocationRepository.ListAsync(new WorkItemInvocationsSpecification(WorkItemId), token),
            items => invocations = items)
        : Task.CompletedTask;

    private void ShowInvocation(AgentInvocation invocation)
    {
        selectedInvocation = invocation;
        activityDetailsOpen = true;
    }

    private static string FormatEffort(string effort) => effort switch
    {
        "xhigh" => "Extra high reasoning",
        _ when effort.Length > 0 => $"{char.ToUpperInvariant(effort[0])}{effort[1..]} reasoning",
        _ => "Reasoning effort not recorded"
    };
}
