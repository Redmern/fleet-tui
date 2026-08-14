using Fleet.Features.Files.BrowseFiles;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;

namespace Fleet.Tests.Features.Files;

public class FileBrowserTests
{
    [Fact]
    public void Choosing_a_folder_asks_yazi_to_write_where_it_ended_up()
    {
        var options = FileBrowser.Choose("C:/repos", "fleet", "w3", "C:/tmp/pick");

        Assert.Equal([FileBrowser.Command, FileBrowser.CwdFlag, "C:/tmp/pick"], options.Args);
        Assert.Equal("C:/repos", options.Cwd);
        Assert.Equal("w3", options.WindowId);
    }

    [Fact]
    public void Browsing_just_opens_yazi_where_the_project_lives()
    {
        var options = FileBrowser.Browse("C:/repos/techweb", "techweb", null);

        Assert.Equal([FileBrowser.Command], options.Args);
        Assert.Equal("C:/repos/techweb", options.Cwd);
    }

    [Fact]
    public void The_chosen_folder_is_the_first_line_of_the_file()
    {
        Assert.Equal("C:/repos/techweb", FileBrowser.Chosen("f", _ => "C:/repos/techweb\n"));
    }

    [Fact]
    public void A_file_yazi_never_wrote_means_nothing_was_chosen()
    {
        Assert.Null(FileBrowser.Chosen("f", _ => null));
        Assert.Null(FileBrowser.Chosen("f", _ => string.Empty));
        Assert.Null(FileBrowser.Chosen("f", _ => "   \n"));
    }

    [Fact]
    public void Yazi_starts_in_the_folder_that_is_already_typed()
    {
        Assert.Equal("C:/repos", FileBrowser.StartIn("C:/repos", p => p == "C:/repos", "C:/home"));
    }

    [Fact]
    public void A_half_typed_path_starts_in_the_parent_that_does_exist()
    {
        var parent = Path.Combine("C:", "repos");

        Assert.Equal(
            parent,
            FileBrowser.StartIn(Path.Combine(parent, "not-yet"), p => p == parent, "C:/home"));
    }

    [Fact]
    public void An_empty_or_unknown_path_starts_at_home()
    {
        Assert.Equal("C:/home", FileBrowser.StartIn(string.Empty, _ => false, "C:/home"));
        Assert.Equal("C:/home", FileBrowser.StartIn("Z:/nope/nope", _ => false, "C:/home"));
    }

    [Fact]
    public void The_file_navigator_has_its_own_key_in_the_menu()
    {
        var map = Keymap.Default;

        Assert.Equal("f", KeymapDefaults.Bindings[FleetAction.BrowseFiles]);
        Assert.NotEqual(map.KeyFor(FleetAction.ViewLogs), map.KeyFor(FleetAction.BrowseFiles));
        Assert.Contains(FleetAction.BrowseFiles, KeymapDefaults.Configurable);
    }
}
