using System.Reflection;

namespace Fleet.Tests.Support;

// Recorded pane screens under Fixtures/panes/<harness>/. They were captured from the real
// harness CLIs in a plain terminal (tmux capture-pane) and scrubbed of paths and names.
public static class PaneCaptures
{
    private static string Root { get; } = Path.Combine(
        typeof(PaneCaptures).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!,
        "tests", "Fleet.Tests", "Fixtures", "panes");

    public static string Read(string harness, string name) =>
        File.ReadAllText(Path.Combine(Root, harness, name + ".txt"));
}
