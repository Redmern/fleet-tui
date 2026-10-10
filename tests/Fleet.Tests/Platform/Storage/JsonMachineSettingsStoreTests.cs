using Fleet.Platform.Storage;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonMachineSettingsStoreTests : ConfigHomeFixture
{
    [Fact]
    public void Iso_is_off_when_the_machine_has_no_settings_file()
    {
        Assert.False(new JsonMachineSettingsStore().LoadIso());
    }

    [Fact]
    public void Iso_round_trips_through_the_machine_file()
    {
        var store = new JsonMachineSettingsStore();

        store.SaveIso(true);
        Assert.True(new JsonMachineSettingsStore().LoadIso());

        store.SaveIso(false);
        Assert.False(new JsonMachineSettingsStore().LoadIso());
    }

    [Fact]
    public void An_unreadable_machine_file_fails_closed_with_iso_on()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FleetPaths.MachineSettingsFile)!);
        File.WriteAllText(FleetPaths.MachineSettingsFile, "{ not json");

        Assert.True(new JsonMachineSettingsStore().LoadIso());
    }

    [Fact]
    public void Saving_project_settings_never_lifts_machine_iso()
    {
        new JsonMachineSettingsStore().SaveIso(true);

        var projects = new JsonSettingsStore();
        Assert.False(projects.Load("techweb").Iso);

        projects.Save("techweb", SettingsConfig.Default.WithIso(false));

        Assert.True(new JsonMachineSettingsStore().LoadIso());
    }

    [Fact]
    public void Project_iso_round_trips_through_the_project_settings()
    {
        var projects = new JsonSettingsStore();

        projects.Save("techweb", SettingsConfig.Default.WithIso(true));

        Assert.True(projects.Load("techweb").Iso);
        Assert.False(projects.Load("other").Iso);
        Assert.False(new JsonMachineSettingsStore().LoadIso());
    }
}
