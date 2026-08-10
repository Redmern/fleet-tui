using System.Reflection;

namespace Fleet.Tests.Features.Projects;

public class PickProjectSelectionTests
{
    [Fact]
    public void The_view_selects_the_first_entry_so_enter_works_without_moving_first()
    {
        var source = File.ReadAllText(
            Path.Combine(RepoRoot, "src", "Fleet", "Features", "Projects", "PickProject",
                "PickProjectView.cs"));

        Assert.Contains("list.SelectedItem = 0;", source);
    }

    private static string RepoRoot { get; } =
        typeof(PickProjectSelectionTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot")
            .Value!;
}
