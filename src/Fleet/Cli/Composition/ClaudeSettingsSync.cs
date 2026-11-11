using Fleet.Ports.Agents.Models;
using Fleet.Ports.Settings;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;

namespace Fleet.Cli.Composition;

public sealed class ClaudeSettingsSync : ISettingsSync
{
    public Result Resync(string project, string projectRoot, SettingsConfig config)
    {
        var result = ClaudeWiring.Sync(project, projectRoot, string.Empty, config.MergedOverDefaults());

        foreach (var agent in ClaudeConfigured(Adapters.Agents().List(project)))
        {
            ClaudeWiring.ResyncWorktree(project, agent.Worktree, agent.Repository, agent.Branch);
        }

        return result;
    }

    public static IEnumerable<AgentRecord> ClaudeConfigured(IEnumerable<AgentRecord> agents) =>
        agents.Where(a => !AgentHarness.IsOrchestrator(a.Harness) && HarnessRegistry.For(a.Harness) == HarnessRegistry.Claude);
}
