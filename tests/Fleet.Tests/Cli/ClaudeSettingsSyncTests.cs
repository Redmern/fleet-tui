using Fleet.Cli.Composition;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Cli;

public sealed class ClaudeSettingsSyncTests
{
    private static AgentRecord Agent(string branch, string harness, bool host = false) =>
        new($"/w/{branch}", "api", branch, harness, "main", false, HostInNvim: host);

    [Fact]
    public void A_settings_sync_rewrites_claude_config_only_where_claude_or_the_editor_runs()
    {
        var agents = new[]
        {
            Agent("claude", AgentHarness.Claude),
            Agent("hosted", AgentHarness.Claude, host: true),
            Agent("editor", AgentHarness.Nvim),
            Agent("opencode", AgentHarness.Opencode),
            Agent("opencode-in-nvim", AgentHarness.Opencode, host: true),
            Agent("sub", AgentHarness.Orchestrator),
        };

        Assert.Equal(
            ["claude", "hosted", "editor"],
            ClaudeSettingsSync.ClaudeConfigured(agents).Select(a => a.Branch));
    }
}
