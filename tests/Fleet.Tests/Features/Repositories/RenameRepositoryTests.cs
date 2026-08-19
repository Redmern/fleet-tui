using Fleet.Features.Repositories;
using Fleet.Features.Repositories.RenameRepository;

namespace Fleet.Tests.Features.Repositories;

public sealed class RenameRepositoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public RenameRepositoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Renaming_moves_the_folder()
    {
        var old = Path.Combine(_root, "old");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "marker.txt"), "x");

        var result = new RenameRepositoryHandler().Handle(_root, old, "new");

        Assert.True(result.Succeeded, result.Error);
        Assert.False(Directory.Exists(old));
        Assert.True(File.Exists(Path.Combine(_root, "new", "marker.txt")));
    }

    [Fact]
    public void Renaming_onto_an_existing_name_is_refused()
    {
        var old = Path.Combine(_root, "old");
        Directory.CreateDirectory(old);
        Directory.CreateDirectory(Path.Combine(_root, "taken"));

        var result = new RenameRepositoryHandler().Handle(_root, old, "taken");

        Assert.False(result.Succeeded);
        Assert.True(Directory.Exists(old));
    }

    [Fact]
    public void A_name_with_a_path_separator_is_refused()
    {
        var old = Path.Combine(_root, "old");
        Directory.CreateDirectory(old);

        var result = new RenameRepositoryHandler().Handle(_root, old, "a/b");

        Assert.False(result.Succeeded);
        Assert.True(Directory.Exists(old));
    }

    [Fact]
    public void The_manage_menu_offers_rename()
    {
        Assert.Contains(RepositoryChores.Entries, e => e.Label == "rename");
        Assert.Equal("rename", RepositoryChores.Entries[RepositoryChores.Rename].Label);
    }
}
