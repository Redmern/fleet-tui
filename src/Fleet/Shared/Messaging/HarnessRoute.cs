using Fleet.Shared.Constants;
using Fleet.Shared.Harness;

namespace Fleet.Shared.Messaging;

public static class HarnessRoute
{
    public const string NoticePrefix = "[fleet] ";

    public static string Of(string harness, bool runsClaude, string orchestrator = AgentHarness.Claude)
    {
        var name = AgentHarness.Normalize(harness);

        if (name == AgentHarness.Orchestrator)
        {
            return orchestrator;
        }

        return name == AgentHarness.Nvim && runsClaude ? AgentHarness.Claude : name;
    }

    public static bool Native(string caller, string target) => !LacksNative(caller) && !LacksNative(target);

    public static string OwnerNotice(string line) =>
        $"{NoticePrefix}{line.ReplaceLineEndings(" ").Trim()} "
        + "(check it with agent_status or list_agents, then review its work).";

    private static bool LacksNative(string harness) =>
        HarnessRegistry.Find(harness) is { } profile && !profile.Has(HarnessCapabilities.NativeMessaging);
}
