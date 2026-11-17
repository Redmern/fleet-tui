using Fleet.Shared.Status;

namespace Fleet.Tests.Shared;

public sealed class ClosingMarkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public ClosingMarkTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void A_folder_is_closing_from_the_write_until_the_clear()
    {
        Assert.False(ClosingMark.Exists(_folder));

        ClosingMark.Write(_folder);

        Assert.True(ClosingMark.Exists(_folder));

        ClosingMark.Clear(_folder);

        Assert.False(ClosingMark.Exists(_folder));
    }

    [Fact]
    public void A_subfolder_of_a_closing_folder_is_covered_by_its_mark()
    {
        var inner = Path.Combine(_folder, "src", "deep");
        Directory.CreateDirectory(inner);

        Assert.Null(ClosingMark.FolderCovering(inner));

        ClosingMark.Write(_folder);

        Assert.Equal(_folder, ClosingMark.FolderCovering(inner));
        Assert.Equal(_folder, ClosingMark.FolderCovering(_folder));
        Assert.Null(ClosingMark.FolderCovering(string.Empty));
    }

    [Fact]
    public void A_folder_that_is_gone_is_not_created_by_the_mark()
    {
        var gone = Path.Combine(_folder, "gone");

        ClosingMark.Write(gone);

        Assert.False(Directory.Exists(gone));
        Assert.False(ClosingMark.Exists(gone));
    }

    [Fact]
    public void Clearing_a_folder_that_was_never_marked_is_fine()
    {
        ClosingMark.Clear(_folder);
        ClosingMark.Clear(string.Empty);

        Assert.False(ClosingMark.Exists(_folder));
    }

    [Fact]
    public void Forgetting_the_pane_mark_removes_only_that_file()
    {
        ClosingMark.Write(_folder);
        PaneQuiet.Observe(_folder, "text", DateTime.UtcNow);
        var mark = Path.Combine(_folder, ".fleet", PaneQuiet.FileName);

        Assert.True(File.Exists(mark));

        PaneQuiet.Forget(_folder);

        Assert.False(File.Exists(mark));
        Assert.True(ClosingMark.Exists(_folder));
    }
}
