using Fleet.Cli.Composition;
using Fleet.Platform.Storage;
using Fleet.Shared.Constants;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Cli;

[Collection(ConfigHomeCollection.Name)]
public sealed class OrchestratorHostSettingsTests : ConfigHomeFixture
{
    [Fact]
    public void Both_default_to_nvim()
    {
        Assert.True(Adapters.MainOrchestratorInNvim("techweb"));
        Assert.True(Adapters.SubOrchestratorsInNvim("techweb"));
    }

    [Fact]
    public void Main_off_with_subs_on_is_read_independently()
    {
        new JsonSettingsStore().Save("techweb", SettingsConfig.Default.WithMainOrchestratorInNvim(false));

        Assert.False(Adapters.MainOrchestratorInNvim("techweb"));
        Assert.True(Adapters.SubOrchestratorsInNvim("techweb"));
    }

    [Fact]
    public void Subs_off_with_main_on_is_read_independently()
    {
        new JsonSettingsStore().Save("techweb", SettingsConfig.Default.WithSubOrchestratorsInNvim(false));

        Assert.True(Adapters.MainOrchestratorInNvim("techweb"));
        Assert.False(Adapters.SubOrchestratorsInNvim("techweb"));
    }

    [Fact]
    public void A_vscode_project_hosts_neither_orchestrator_in_nvim()
    {
        new JsonSettingsStore().Save("techweb", SettingsConfig.Default.WithEditor(Editor.Vscode));

        Assert.False(Adapters.MainOrchestratorInNvim("techweb"));
        Assert.False(Adapters.SubOrchestratorsInNvim("techweb"));
    }

    [Fact]
    public void A_vscode_editor_keeps_the_stored_in_nvim_settings()
    {
        var config = SettingsConfig.Default.WithEditor(Editor.Vscode);

        Assert.True(config.MainOrchestratorInNvim);
        Assert.True(config.SubOrchestratorsInNvim);
        Assert.False(config.MainOrchestratorHostedInNvim);
        Assert.False(config.SubOrchestratorsHostedInNvim);
    }

    [Fact]
    public void An_nvim_project_hosts_orchestrators_as_its_settings_say()
    {
        var config = SettingsConfig.Default.WithEditor(Editor.Nvim).WithSubOrchestratorsInNvim(false);

        Assert.True(config.MainOrchestratorHostedInNvim);
        Assert.False(config.SubOrchestratorsHostedInNvim);
    }

    [Fact]
    public void New_agents_open_bare_claude_in_a_vscode_project_and_nvim_otherwise()
    {
        Assert.Equal(AgentHarness.Claude, SettingsConfig.Default.WithEditor(Editor.Vscode).DefaultAgentHarness);
        Assert.Equal(AgentHarness.Nvim, SettingsConfig.Default.DefaultAgentHarness);
    }
}
