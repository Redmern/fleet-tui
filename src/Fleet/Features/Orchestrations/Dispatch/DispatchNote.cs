namespace Fleet.Features.Orchestrations.Dispatch;

public static class DispatchNote
{
    public const string Nothing = "fleet: nothing to dispatch — the prompt was empty.";

    public static string Dispatched(string slug) =>
        $"fleet: dispatched sub-orchestrator '{slug}' (hidden). It has the fleet MCP "
        + "tools and this project's permission settings. Open it from the dashboard's Subs tab.";
}
