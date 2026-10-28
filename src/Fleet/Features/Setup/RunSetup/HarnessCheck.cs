using Fleet.Features.Setup.RunSetup.Models;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;

namespace Fleet.Features.Setup.RunSetup;

public static class HarnessCheck
{
    private static readonly (HarnessCapabilities Capability, string Without)[] Degradations =
    [
        (HarnessCapabilities.Hooks, "no status hooks: status comes from report and pane activity"),
        (HarnessCapabilities.Permissions, "no permission policy: fleet's tool rules are not written for it"),
        (HarnessCapabilities.Skills, "no skills"),
        (HarnessCapabilities.NativeMessaging, "no cross-session messages: tell_agent is typed into its pane"),
    ];

    public static IReadOnlyList<string> Degraded(HarnessProfile profile) =>
        [.. Degradations.Where(d => !profile.Has(d.Capability)).Select(d => d.Without)];

    public static IReadOnlyList<HarnessProfile> Extra(IReadOnlyCollection<string> checkedAlready) =>
        [.. HarnessRegistry.Profiles.Where(p => !checkedAlready.Contains(p.Binary))];

    public static SetupStep Step(HarnessProfile profile, bool found, bool inUse) =>
        (found, inUse) switch
        {
            (true, _) => new SetupStep(profile.Binary, true, inUse ? "found on PATH" : "found on PATH, not in use"),
            (false, true) => new SetupStep(
                profile.Binary, false, $"missing - its {profile.Name} agents cannot start", false, SetupHints.For(profile.Binary)),
            _ => new SetupStep(
                profile.Binary, true, $"not installed, not in use; {SetupHints.For(profile.Binary)} to use {profile.Name}"),
        };

    public static IReadOnlyDictionary<string, int> InUse(IEnumerable<AgentRecord> agents) =>
        agents
            .Select(a => AgentHarness.IsOrchestrator(a.Harness) ? HarnessRegistry.Claude.Name : a.Harness)
            .Where(AgentHarness.RunsHarness)
            .GroupBy(h => h, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    public static IReadOnlyList<string> DoctorLines(Func<string, bool> onPath, IReadOnlyDictionary<string, int> inUse) =>
        [.. HarnessRegistry.Profiles.Select(p => DoctorLine(p, onPath(p.Binary), inUse.GetValueOrDefault(p.Name)))];

    private static string DoctorLine(HarnessProfile profile, bool found, int agents)
    {
        var use = agents > 0 ? $"used by {agents} agent(s)" : "not in use";
        var degraded = Degraded(profile);
        var path = found ? "on PATH" : agents > 0 ? $"NOT FOUND ({SetupHints.For(profile.Binary)})" : "NOT FOUND";

        return $"{profile.Name,-13} {path}, {use}"
            + (degraded.Count == 0 ? string.Empty : "; " + string.Join("; ", degraded));
    }
}
