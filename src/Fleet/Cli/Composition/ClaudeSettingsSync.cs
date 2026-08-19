using Fleet.Ports.Settings;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;

namespace Fleet.Cli.Composition;

public sealed class ClaudeSettingsSync : ISettingsSync
{
    public Result Resync(string project, string projectRoot, SettingsConfig config)
    {
        var result = ClaudeWiring.Sync(project, projectRoot, string.Empty, config.MergedOverDefaults());

        foreach (var agent in Adapters.Agents().List(project)
            .Where(a => !AgentHarness.IsOrchestrator(a.Harness)))
        {
            ClaudeWiring.ResyncWorktree(project, agent.Worktree, agent.Repository, agent.Branch);
        }

        return result;
    }
}
