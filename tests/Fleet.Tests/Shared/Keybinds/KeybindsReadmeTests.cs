using System.Reflection;
using System.Text;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keybinds.Models;

namespace Fleet.Tests.Shared.Keybinds;

/// <summary>
/// The README's shared-keys table is generated from the shipped keybind model, between
/// the begin/end markers. Run the tests with FLEET_WRITE_README=1 to rewrite it after
/// changing keybinds.default.json.
/// </summary>
public class KeybindsReadmeTests
{
    private const string Begin = "<!-- keybinds:begin (generated from keybinds.default.json, see KeybindsReadmeTests) -->";

    private const string End = "<!-- keybinds:end -->";

    private static readonly KeybindTarget[] Columns = [KeybindTarget.Mux, KeybindTarget.Nvim, KeybindTarget.Claude];

    private static string RepoRoot { get; } =
        typeof(KeybindsReadmeTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "RepoRoot")?.Value
        ?? throw new InvalidOperationException(
            "RepoRoot assembly metadata is missing - see Fleet.Tests.csproj");

    private static string ReadmePath => Path.Combine(RepoRoot, "README.md");

    [Fact]
    public void The_readme_shared_keys_table_matches_the_shipped_model()
    {
        var raw = File.ReadAllText(ReadmePath);
        var readme = raw.ReplaceLineEndings("\n");
        var start = readme.IndexOf(Begin, StringComparison.Ordinal);
        var end = readme.IndexOf(End, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, $"README.md needs the markers {Begin} and {End}");

        var current = readme[(start + Begin.Length)..end];
        var wanted = "\n" + Table(KeybindDefaults.Set) + "\n";

        if (current != wanted && Environment.GetEnvironmentVariable("FLEET_WRITE_README") == "1")
        {
            var updated = readme[..(start + Begin.Length)] + wanted + readme[end..];
            File.WriteAllText(ReadmePath, raw.Contains("\r\n", StringComparison.Ordinal) ? updated.ReplaceLineEndings("\r\n") : updated);
            return;
        }

        Assert.True(
            current == wanted,
            "README.md's shared keys table is out of date; run the tests with FLEET_WRITE_README=1, or paste:\n" + wanted);
    }

    [Fact]
    public void The_table_lists_every_entry_that_reaches_nvim_or_claude()
    {
        var table = Table(KeybindDefaults.Set);

        Assert.Contains("| `Alt+h` | `resize-left` | direct | n, t | - |", table);
        Assert.Contains("| `Alt+n` | `claude-normal-mode` | - | t | - |", table);
        Assert.Contains("| `Shift+Enter` | `newline` | direct | - | Chat |", table);
        Assert.DoesNotContain("open-menu", table);
    }

    private static string Table(KeybindSet set)
    {
        var text = new StringBuilder();

        text.Append("| Chord | Action | mux | nvim | claude |\n");
        text.Append("|---|---|---|---|---|");

        foreach (var entry in set.Entries.Where(Shared))
        {
            text.Append('\n')
                .Append($"| {Chord(entry)} | `{entry.Action}` | ")
                .Append(string.Join(" | ", Columns.Select(c => Contexts(entry, c))))
                .Append(" |");
        }

        return text.ToString();
    }

    private static bool Shared(KeybindEntry entry) =>
        entry.Targets.ContainsKey(KeybindTarget.Nvim) || entry.Targets.ContainsKey(KeybindTarget.Claude);

    private static string Chord(KeybindEntry entry)
    {
        var chord = $"`{entry.Chord}`";

        return entry.OsChords.Count == 0
            ? chord
            : chord + " (" + string.Join(", ", entry.OsChords.Select(o => $"{KeybindNames.Of(o.Key)}: `{o.Value}`")) + ")";
    }

    private static string Contexts(KeybindEntry entry, KeybindTarget target) =>
        !entry.Targets.TryGetValue(target, out var contexts) ? "-"
        : contexts.Count == 0 ? "yes"
        : string.Join(", ", contexts);
}
