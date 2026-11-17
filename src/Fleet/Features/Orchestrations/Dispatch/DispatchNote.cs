using Fleet.Ports.Mux.Enums;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Features.Orchestrations.Dispatch;

public static class DispatchNote
{
    public const string Nothing = "fleet: nothing to dispatch — the prompt was empty.";

    public static string Dispatched(string slug, Profile? profile = null) =>
        $"fleet: dispatched sub-orchestrator '{slug}' (hidden"
        + (profile is { } p ? $", Ai-DLC {Words.Of(p)}" : string.Empty)
        + "). It has the fleet MCP tools and this project's permission settings. Open it from the dashboard's Subs tab.";

    public static string KickoffNotConfirmed(PromptDelivery delivery) =>
        $" Its first task is written to .fleet/instruction.md, but delivery was not confirmed ({Reason(delivery)}).";

    public static bool Confirmed(PromptDelivery delivery) => delivery is PromptDelivery.Echoed or PromptDelivery.Pumped;

    public static string Reason(PromptDelivery delivery) => delivery switch
    {
        PromptDelivery.Lost =>
            "its pane never showed the typed prompt (a pane fleet cannot read looks the same), so fleet left it unsubmitted",
        PromptDelivery.Deadline =>
            "fleet stopped waiting for the prompt to show, before the caller's tool call would time out, and pressed Enter once",
        PromptDelivery.PumpUnseen =>
            "fleet stopped waiting for nvim to report it; nvim delivers it once the agent is ready",
        PromptDelivery.PumpUnconfirmed =>
            "nvim typed it twice, could not see it arrive, cleared the line and left it unsubmitted; send it again with tell_agent",
        PromptDelivery.Unchecked =>
            "its harness has no check for typed input and its MCP server never reported ready",
        _ => string.Empty,
    };

    public const string NoDirect =
        "fleet: this dispatch path can't start a repository agent directly; leave out the repository.";

    public static string Direct(string repository, string branch, string started) =>
        $"fleet: no sub-orchestrator for this one; started a repository agent on {repository}/{branch} with the task. "
        + started;

    public static string RepositoryLeftToSub(bool research) =>
        research
            ? " The repository was not used: research stays with the sub-orchestrator."
            : " The repository was not used: Ai-DLC applies, so the sub-orchestrator starts the agents.";

    public static string UnknownHarness(string refusal) => $"fleet: {refusal}";

    public static string ConfigNotWritten(string harness, string reason) =>
        $" Warning: the {harness} configuration for it was not fully written ({reason}), "
        + "so it may not reach the fleet tools.";

    public static string UnknownProfile(string given) =>
        $"fleet: '{given.Trim()}' is not an Ai-DLC profile. Use one of: "
        + string.Join(", ", ProfileCatalog.All.Select(p => Words.Of(p)))
        + ".";
}
