using Fleet.Features.Repositories.OpenRepository;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Features.Repositories;

public sealed class OpenRepositoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    public OpenRepositoryTests() => Directory.CreateDirectory(_root);

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

    private string ProjectRoot => Path.Combine(_root, "techweb");

    private string Worktree(string name, string branch)
    {
        var path = Path.Combine(ProjectRoot, name, branch);
        Directory.CreateDirectory(path);

        return path;
    }

    [Fact]
    public async Task An_open_repository_in_another_window_is_brought_here_rather_than_ignored()
    {
        var worktree = Worktree("frontend", "develop");

        var dashboard = await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });
        var stray = await _mux.SpawnAsync(new SpawnOptions { Cwd = worktree, NewWindow = true });

        var before = await _mux.ListPanesAsync();
        var home = before.Single(p => p.Id == dashboard).WindowId;

        Assert.NotEqual(home, before.Single(p => p.Id == stray).WindowId);

        var result = await new OpenRepositoryHandler(_mux).HandleAsync(
            "techweb",
            "frontend",
            Path.Combine(ProjectRoot, "frontend"),
            "develop",
            ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var after = await _mux.ListPanesAsync();

        Assert.Equal(home, after.Single(p => p.Id == stray).WindowId);
        Assert.True(after.Single(p => p.Id == stray).IsActive);
        Assert.Equal("frontend/develop", _mux.TitleOf(stray));
        Assert.Equal(2, after.Count);
    }

    [Fact]
    public async Task A_repository_that_is_not_open_yet_gets_a_pane_in_the_dashboards_window()
    {
        Worktree("backend", "develop");

        var dashboard = await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });

        var result = await new OpenRepositoryHandler(_mux).HandleAsync(
            "techweb",
            "backend",
            Path.Combine(ProjectRoot, "backend"),
            "develop",
            ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();
        var home = panes.Single(p => p.Id == dashboard).WindowId;

        Assert.Equal(2, panes.Count);
        Assert.All(panes, p => Assert.Equal(home, p.WindowId));
    }
}
