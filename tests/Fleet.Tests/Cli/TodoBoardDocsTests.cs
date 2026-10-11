using System.Reflection;

namespace Fleet.Tests.Cli;

public class TodoBoardDocsTests
{
    private static string RepoRoot { get; } =
        typeof(TodoBoardDocsTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;

    private static string Readme => File.ReadAllText(Path.Combine(RepoRoot, "README.md"));

    private static string LatestNotes
    {
        get
        {
            var notes = File.ReadAllText(Path.Combine(RepoRoot, "RELEASE_NOTES.md"));
            var start = notes.IndexOf("\n## ", StringComparison.Ordinal);
            var end = notes.IndexOf("\n## ", start + 1, StringComparison.Ordinal);

            return notes[start..end];
        }
    }

    [Theory]
    [InlineData("All todos")]
    [InlineData("ctrl+s a T")]
    [InlineData("todo board of all projects")]
    public void The_readme_no_longer_mentions_the_separate_all_projects_board(string retired) =>
        Assert.DoesNotContain(retired, Readme, StringComparison.Ordinal);

    [Fact]
    public void The_readme_describes_the_scope_switch() =>
        Assert.Contains("`a` switches the board between this project and all projects", Readme, StringComparison.Ordinal);

    [Fact]
    public void The_latest_release_notes_describe_the_scope_switch_and_not_capital_t()
    {
        Assert.DoesNotContain("ctrl+s a T", LatestNotes, StringComparison.Ordinal);
        Assert.Contains("`a` in the board switches to all projects", LatestNotes, StringComparison.Ordinal);
    }
}
