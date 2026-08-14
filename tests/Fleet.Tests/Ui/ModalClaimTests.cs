using System.Reflection;

namespace Fleet.Tests.Ui;

public class ModalClaimTests
{
    private static string RepoRoot { get; } =
        typeof(ModalClaimTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot")
            .Value!;

    // A view that runs its own window while another one listens on
    // app.Keyboard.KeyDown must claim the keys, or the view underneath keeps
    // acting on letters the user is typing into a field.
    [Fact]
    public void Every_view_that_runs_a_window_claims_the_keys()
    {
        var offenders = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src", "Fleet"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("app.Run(window)", StringComparison.Ordinal))
            .Where(f => !File.ReadAllText(f).Contains("FleetModal.Enter", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Where(f => f != "ShowDashboardView.cs")
            .ToList();

        Assert.Empty(offenders);
    }
}
