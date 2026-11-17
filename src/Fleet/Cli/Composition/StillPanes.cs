using Fleet.Ports.Agents.Models;
using Fleet.Shared.Status;

namespace Fleet.Cli.Composition;

public sealed class StillPanes
{
    private readonly Dictionary<string, (string Text, DateTime Since)> _seen = new(StringComparer.OrdinalIgnoreCase);

    public DateTime Since(AgentRecord agent, string paneText, DateTime now)
    {
        var settled = PaneIdleRule.Settled(agent.RunningHarness, paneText);

        if (!_seen.TryGetValue(agent.Worktree, out var still) || still.Text != settled)
        {
            _seen[agent.Worktree] = still = (settled, now);
        }

        return still.Since;
    }

    public void Forget(string worktree) => _seen.Remove(worktree);
}
