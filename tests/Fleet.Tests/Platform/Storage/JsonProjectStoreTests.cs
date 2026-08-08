using Fleet.Platform.Storage;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonProjectStoreTests : ConfigHomeFixture
{
    private static JsonProjectStore Store => new();

    private string ARoot
    {
        get
        {
            var dir = Path.Combine(ConfigHome, "root");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var root = ARoot;
        Store.Save(new Project("backend", root));

        var loaded = Store.Load("backend");

        Assert.NotNull(loaded);
        Assert.Equal("backend", loaded!.Name);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), loaded.Root);
    }

    [Fact]
    public void Load_returns_null_for_an_unknown_project()
        => Assert.Null(Store.Load("nope"));

    [Fact]
    public void Load_returns_null_for_a_name_that_sanitizes_to_nothing()
        => Assert.Null(Store.Load("..."));

    [Fact]
    public void List_returns_projects_sorted_by_name()
    {
        var root = ARoot;
        Store.Save(new Project("zeta", root));
        Store.Save(new Project("alpha", root));

        Assert.Equal(["alpha", "zeta"], Store.List().Select(p => p.Name));
    }

    [Fact]
    public void List_is_empty_before_anything_is_saved()
        => Assert.Empty(Store.List());

    [Fact]
    public void Save_rejects_a_name_that_sanitizes_to_nothing()
        => Assert.Throws<ArgumentException>(() => Store.Save(new Project("...", ARoot)));

    [Fact]
    public void Save_rejects_a_root_that_is_not_a_directory()
        => Assert.Throws<DirectoryNotFoundException>(
            () => Store.Save(new Project("x", Path.Combine(ConfigHome, "missing"))));

    [Fact]
    public void List_skips_an_unreadable_file_rather_than_failing()
    {
        Store.Save(new Project("good", ARoot));
        File.WriteAllText(Path.Combine(FleetPaths.Projects, "broken.json"), "{ not json");

        Assert.Equal(["good"], Store.List().Select(p => p.Name));
    }

    [Fact]
    public void Remove_deletes_a_project_and_is_silent_about_an_unknown_one()
    {
        Store.Save(new Project("gone", ARoot));
        Store.Remove("gone");
        Store.Remove("never-existed");

        Assert.Null(Store.Load("gone"));
    }

    [Fact]
    public void Save_replaces_an_existing_project_of_the_same_name()
    {
        var first = Path.Combine(ConfigHome, "one");
        var second = Path.Combine(ConfigHome, "two");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(second);

        Store.Save(new Project("dup", first));
        Store.Save(new Project("dup", second));

        Assert.Equal(Path.TrimEndingDirectorySeparator(second), Store.Load("dup")!.Root);
        Assert.Single(Store.List());
    }
}
