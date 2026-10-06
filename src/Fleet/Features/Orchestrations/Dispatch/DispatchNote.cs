using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;

namespace Fleet.Features.Orchestrations.Dispatch;

public static class DispatchNote
{
    public const string Nothing = "fleet: nothing to dispatch — the prompt was empty.";

    public static string Dispatched(string slug, Profile? profile = null) =>
        $"fleet: dispatched sub-orchestrator '{slug}' (hidden"
        + (profile is { } p ? $", AIDLC {Words.Of(p)}" : string.Empty)
        + "). It has the fleet MCP tools and this project's permission settings. Open it from the dashboard's Subs tab.";

    public const string NoDirect =
        "fleet: this dispatch path can't start a repository agent directly; leave out the repository.";

    public static string Direct(string repository, string branch, string started) =>
        $"fleet: no sub-orchestrator for this one; started a repository agent on {repository}/{branch} with the task. "
        + started;

    public static string RepositoryLeftToSub(bool research) =>
        research
            ? " The repository was not used: research stays with the sub-orchestrator."
            : " The repository was not used: AIDLC applies, so the sub-orchestrator starts the agents.";

    public static string UnknownProfile(string given) =>
        $"fleet: '{given.Trim()}' is not an AIDLC profile. Use one of: "
        + string.Join(", ", ProfileCatalog.All.Select(p => Words.Of(p)))
        + ".";
}
