using System.Text.RegularExpressions;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;

namespace Fleet.Tests.Docs;

// README.md and docs/DESIGN.md list fleet's keys in tables. Each key row is compared with the code:
// the dashboard keymap (KeymapDefaults) and the shipped keybind model (keybinds.default.json) for
// the multiplexer's direct and prefix keys. Tables are picked by their header, never by their
// backticks, so a status-glyph or MCP-tool table is not mistaken for a key table; a table whose
// header looks like a key table but is neither checked nor excluded here fails the test.
// KeyExpectations holds what each key is expected to do, by hand.
public class KeyTablesTests
{
    private static readonly IReadOnlyList<(string File, string First, string Second, string Why)> Excluded =
    [
        ("README.md", "Chord", "Action", "generated from keybinds.default.json and checked by KeybindsReadmeTests"),
        ("README.md", "Key", "In the Custom tab", "keys of the theme picker's Custom tab: a view's own keys, not keymap actions"),
        ("README.md", "Key", "In the editor", "keys of the theme editor: a view's own keys, not keymap actions"),
        ("README.md", "Key", "In the color dialog", "keys of the color dialog: a view's own keys, not keymap actions"),
        ("docs/DESIGN.md", "Key", "Byte", "bytes measured in a WezTerm pane for the 2026-08-08 verification log: a fact, not a binding"),
    ];

    private static readonly IReadOnlyList<(string File, string First, string Second)> Checked =
    [
        ("README.md", "Key", "Does"),
        ("README.md", "Group", "Keys"),
        ("docs/DESIGN.md", "Prefix key", "Does"),
        ("docs/DESIGN.md", "Key without the prefix", "Does"),
    ];

    private static IReadOnlyList<KeybindEntryView> Direct { get; } = Entries("direct");

    private static IReadOnlyList<KeybindEntryView> Prefixed { get; } = Entries("prefix");

    private static IReadOnlyList<KeybindEntryView> Entries(string context) =>
    [
        .. KeybindDefaults.Set.Entries
            .Where(e => e.Targets.TryGetValue(KeybindTarget.Mux, out var contexts) && contexts.Contains(context))
            .Select(e => new KeybindEntryView(e.Chord, e.Action)),
    ];

    public static TheoryData<string, string> Sentinels => new()
    {
        { "README keys", "ctrl+d" },
        { "DESIGN direct keys", "ctrl+enter" },
        { "README prefix", "a d" },
        { "DESIGN prefix", "a d" },
    };

    private static IEnumerable<string> Expand(string token)
    {
        var letters = Regex.Match(token, "^(.*\\+)(.)((?:/.)+)$");

        return letters.Success
            ? new[] { letters.Groups[2].Value }.Concat(letters.Groups[3].Value.Split('/', StringSplitOptions.RemoveEmptyEntries)).Select(l => letters.Groups[1].Value + l)
            : [token];
    }

    private static string? DirectProblem(string chord)
    {
        var name = chord.Replace("←", "Left", StringComparison.Ordinal).Replace("→", "Right", StringComparison.Ordinal);

        return Direct.Any(d => KeyExpectations.SameKey(name, d.Chord))
            ? null
            : $"`{chord}` is not a key the table test knows: add it to KeyExpectations.DashboardKeys with its actions, or it is not a direct multiplexer key in keybinds.default.json";
    }

    private static string? KeyProblem(string token)
    {
        if (KeyExpectations.NotInTheKeymap.ContainsKey(token))
        {
            return null;
        }

        if (token.Equals("ctrl+enter", StringComparison.OrdinalIgnoreCase))
        {
            return KeyExpectations.SameKey(token, KeymapDefaults.Prefix) ? null : $"`{token}` is not the menu prefix ({KeymapDefaults.Prefix})";
        }

        if (KeyExpectations.DashboardKeys.FirstOrDefault(k => KeyExpectations.SameKey(token, k.Key)) is { Value: { } actions })
        {
            var wrong = actions
                .Select(a => (Action: a, Bound: KeymapDefaults.Bindings.GetValueOrDefault(a)))
                .Where(b => b.Bound is null || !KeyExpectations.SameKey(token, b.Bound))
                .Select(b => $"{b.Action} is bound to `{b.Bound ?? "nothing"}`");

            return wrong.Any() ? $"`{token}` does not match the dashboard keymap: {string.Join(", ", wrong)}" : null;
        }

        return Expand(token).Select(DirectProblem).FirstOrDefault(p => p is not null);
    }

    private static string? PrefixProblem(string chord)
    {
        var shipped = chord.Equals("space", StringComparison.Ordinal) ? "Space" : chord;
        var bound = Prefixed.FirstOrDefault(p => p.Chord == shipped);

        if (bound is null)
        {
            return Prefixed.Any(p => p.Chord.StartsWith(shipped + " ", StringComparison.Ordinal))
                ? null
                : $"prefix key `{chord}` is not in keybinds.default.json";
        }

        if (!KeyExpectations.PrefixActions.TryGetValue(chord, out var expected))
        {
            return $"prefix key `{chord}` has no expected action in KeyExpectations.PrefixActions";
        }

        return bound.Action == expected
            ? null
            : $"prefix key `{chord}` is documented as `{expected}` but keybinds.default.json binds it to `{bound.Action}`";
    }

    private static void NoProblems(string table, IEnumerable<string?> problems)
    {
        var all = problems.OfType<string>().ToList();

        Assert.True(all.Count == 0, table + ":\n" + string.Join('\n', all));
    }

    [Theory]
    [MemberData(nameof(Sentinels))]
    public void A_table_that_parses_to_nothing_is_a_failure_not_a_pass(string table, string sentinel)
    {
        var keys = table switch
        {
            "README keys" => DocTables.ReadmeKeys,
            "DESIGN direct keys" => DocTables.DesignDirectKeys,
            "README prefix" => DocTables.ReadmePrefixChords,
            "DESIGN prefix" => DocTables.DesignPrefixChords,
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "unknown table"),
        };

        Assert.Contains(sentinel, keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_key_in_the_readme_keys_table_is_a_dashboard_or_multiplexer_key() =>
        NoProblems("README.md Keys table", DocTables.ReadmeKeys.Select(KeyProblem));

    [Fact]
    public void Every_key_in_designs_keys_without_the_prefix_table_is_a_direct_or_dashboard_key() =>
        NoProblems("docs/DESIGN.md 'Key without the prefix' table", DocTables.DesignDirectKeys.Select(KeyProblem));

    [Fact]
    public void Every_prefix_key_in_the_readme_group_table_runs_the_action_the_model_binds() =>
        NoProblems("README.md prefix group table", DocTables.ReadmePrefixChords.Select(PrefixProblem));

    [Fact]
    public void Every_prefix_key_in_designs_prefix_table_runs_the_action_the_model_binds() =>
        NoProblems("docs/DESIGN.md prefix table", DocTables.DesignPrefixChords.Select(PrefixProblem));

    [Fact]
    public void A_g_row_documents_move_first_and_not_move_last()
    {
        var documented = KeyExpectations.ActionsDocumentedBy(["g"]);

        Assert.Contains(FleetAction.MoveFirst, documented);
        Assert.DoesNotContain(FleetAction.MoveLast, documented);
    }

    [Fact]
    public void A_table_that_looks_like_a_key_table_is_checked_or_excluded_with_a_reason()
    {
        var unknown = DocTables.KeyTableHeaders
            .Where(h => !Checked.Any(c => Matches(h, c.File, c.First, c.Second)) && !Excluded.Any(e => Matches(h, e.File, e.First, e.Second)))
            .Select(h => $"{h.File} has a table '{h.First} | {h.Second}' that KeyTablesTests neither checks nor excludes");

        NoProblems("key tables", unknown);
    }

    [Fact]
    public void Every_exclusion_gives_a_reason()
    {
        Assert.All(Excluded, e => Assert.False(string.IsNullOrWhiteSpace(e.Why), $"{e.File} '{e.First} | {e.Second}' has no reason"));
    }

    private static bool Matches((string File, string First, string Second) header, string file, string first, string second) =>
        header.File == file && header.First.StartsWith(first, StringComparison.Ordinal) && header.Second == second;

    private sealed record KeybindEntryView(string Chord, string Action);
}
