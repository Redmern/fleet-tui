using Fleet.Shared.Constants;

namespace Fleet.Ports.Approvals.Models;

public sealed record ApprovalRequest(
    string Project,
    string Tool,
    string Summary,
    string? Pane = null,
    string Caller = "",
    TimeSpan? Timeout = null)
{
    public static readonly TimeSpan DefaultWait = TimeSpan.FromSeconds(120);

    public string CallerName => Caller.Length == 0 ? AgentHarness.Orchestrator : Caller;
}
