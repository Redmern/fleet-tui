using System.Reflection;

namespace Fleet.Tests.Cli;

public class OpenProjectFocusTests
{
    private static string RepoRoot { get; } =
        typeof(OpenProjectFocusTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot")
            .Value!;

    private static readonly string Source = File.ReadAllText(
        Path.Combine(RepoRoot, "src", "Fleet", "Cli", "Commands", "PickProjectCommand.cs"));

    [Fact]
    public void Focus_returns_to_the_main_pane_after_the_restored_agents_have_spawned()
    {
        var restore = Source.IndexOf("RestoreSessionHandler", StringComparison.Ordinal);
        var focus = Source.IndexOf("FocusPaneAsync(result.Value.DashPane)", StringComparison.Ordinal);

        Assert.True(restore > 0, "opening a project should restore the session");
        Assert.True(focus > restore, "restoring spawns panes, so focus has to be re-asserted after");
    }
}
