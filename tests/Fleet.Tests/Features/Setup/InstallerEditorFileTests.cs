using Fleet.Platform.Storage;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Tests.Features.Setup;

// install.sh and scripts/deps.ps1 write editor.json by hand; these are their exact bytes.
[Collection(ConfigHomeCollection.Name)]
public sealed class InstallerEditorFileTests : ConfigHomeFixture
{
    [Theory]
    [InlineData("{\n  \"version\": 1,\n  \"editor\": \"vscode\"\n}\n", Editor.Vscode)]
    [InlineData("{\n  \"version\": 1,\n  \"editor\": \"nvim\"\n}\n", Editor.Nvim)]
    [InlineData("{\r\n  \"version\": 1,\r\n  \"editor\": \"vscode\"\r\n}\r\n", Editor.Vscode)]
    public void The_editor_json_an_installer_writes_is_the_global_default_fleet_reads(string written, Editor expected)
    {
        FleetPaths.EnsureDirs();
        File.WriteAllText(FleetPaths.EditorSettingsFile, written);

        var store = new JsonSettingsStore();

        Assert.Equal(expected, store.LoadDefaultEditor());
        Assert.Equal(expected, store.Load("techweb").ResolvedEditor);
    }

    [Fact]
    public void Fleet_writes_the_same_shape_the_installers_write()
    {
        new JsonSettingsStore().SaveDefaultEditor(Editor.Vscode);

        Assert.Equal(
            "{\n  \"version\": 1,\n  \"editor\": \"vscode\"\n}",
            File.ReadAllText(FleetPaths.EditorSettingsFile).ReplaceLineEndings("\n").TrimEnd());
    }
}
