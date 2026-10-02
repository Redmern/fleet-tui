using Fleet.Cli.Composition;
using Fleet.Platform.Storage;
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
}
