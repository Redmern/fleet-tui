using System.Text.Json;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keybinds.Models;

namespace Fleet.Tests.Shared.Keybinds;

public class KeybindLayeringTests
{
    private readonly List<string> _log = [];

    private KeybindSet Apply(string json) =>
        KeybindLayering.Apply(KeybindDefaults.Set, KeybindLayering.Parse(json).Keybinds, _log.Add);

    [Fact]
    public void A_diff_changes_only_the_fields_it_names()
    {
        var set = Apply("""{ "keybinds": { "resize-left": { "chord": "alt+shift+h" } } }""");

        var entry = set.Find("resize-left")!;
        Assert.Equal("Alt+Shift+h", entry.Chord);
        Assert.Equal("resize-left", entry.Action);
        Assert.Equal(["n", "t"], entry.Targets[KeybindTarget.Nvim]);
        Assert.Empty(_log);
    }

    [Fact]
    public void None_unbinds_an_entry_for_every_target()
    {
        var set = Apply("""{ "keybinds": { "focus-left": { "chord": "none" } } }""");

        Assert.NotNull(set.Find("focus-left"));
        Assert.DoesNotContain(set.For(KeybindTarget.Mux, KeybindOs.Windows), b => b.Id == "focus-left");
        Assert.DoesNotContain(set.For(KeybindTarget.Nvim, KeybindOs.Windows), b => b.Id == "focus-left");
    }

    [Fact]
    public void None_also_drops_per_os_chords_unless_the_diff_names_them()
    {
        var set = Apply("""{ "keybinds": { "resize-left": { "os": { "macos": "ctrl+alt+h" } } } }""");
        set = KeybindLayering.Apply(set, KeybindLayering.Parse("""{ "keybinds": { "resize-left": { "chord": "none" } } }""").Keybinds);

        Assert.False(set.Find("resize-left")!.IsBoundOn(KeybindOs.MacOs));
    }

    [Fact]
    public void A_per_os_chord_applies_on_that_os_only()
    {
        var set = Apply("""{ "keybinds": { "resize-left": { "os": { "macos": "ctrl+alt+h", "linux": "none" } } } }""");

        Assert.Equal("Ctrl+Alt+h", Single(set, KeybindOs.MacOs).Chord);
        Assert.Equal("Alt+h", Single(set, KeybindOs.Windows).Chord);
        Assert.DoesNotContain(set.For(KeybindTarget.Nvim, KeybindOs.Linux), b => b.Id == "resize-left");
    }

    [Fact]
    public void A_new_id_with_action_chord_and_targets_is_added()
    {
        var set = Apply("""
            {
              // comments and trailing commas are fine
              "keybinds": {
                "my-zoom": { "action": "zoom", "chord": "ctrl+z", "targets": { "mux": ["direct"] }, },
              },
            }
            """);

        var binding = Assert.Single(set.For(KeybindTarget.Mux, KeybindOs.Windows), b => b.Id == "my-zoom");
        Assert.Equal("Ctrl+z", binding.Chord);
        Assert.Equal(["direct"], binding.Contexts);
    }

    [Fact]
    public void A_new_id_without_targets_is_ignored_and_logged()
    {
        var set = Apply("""{ "keybinds": { "my-zoom": { "action": "zoom", "chord": "ctrl+z" } } }""");

        Assert.Null(set.Find("my-zoom"));
        Assert.Contains(_log, l => l.Contains("my-zoom", StringComparison.Ordinal));
    }

    [Fact]
    public void Targets_replace_the_whole_target_list_and_unknown_targets_are_logged()
    {
        var set = Apply("""{ "keybinds": { "focus-left": { "targets": { "nvim": ["n"], "wezterm": [] } } } }""");

        var entry = set.Find("focus-left")!;
        Assert.Equal([KeybindTarget.Nvim], entry.Targets.Keys);
        Assert.Equal(["n"], entry.Targets[KeybindTarget.Nvim]);
        Assert.Contains(_log, l => l.Contains("wezterm", StringComparison.Ordinal));
    }

    [Fact]
    public void A_bad_chord_leaves_the_entry_as_it_was()
    {
        var set = Apply("""{ "keybinds": { "focus-left": { "chord": "hyper+h" } } }""");

        Assert.Equal("Ctrl+h", set.Find("focus-left")!.Chord);
        Assert.Single(_log);
    }

    [Theory]
    [InlineData("""{ "keybinds": { "focus-left": { "chord": "" } } }""")]
    [InlineData("""{ "keybinds": { "focus-left": { "targets": {} } } }""")]
    [InlineData("""{ "keybinds": { "focus-left": { "targets": { "wezterm": [] } } } }""")]
    public void An_empty_chord_or_no_known_target_is_rejected_not_taken_as_unbind(string json)
    {
        var set = Apply(json);

        Assert.Equal(KeybindDefaults.Set.Find("focus-left"), set.Find("focus-left"));
        Assert.NotEmpty(_log);
    }

    [Fact]
    public void Parse_rejects_broken_json()
    {
        Assert.ThrowsAny<JsonException>(() => KeybindLayering.Parse("{ not json"));
    }

    private static KeybindBinding Single(KeybindSet set, KeybindOs os) =>
        Assert.Single(set.For(KeybindTarget.Nvim, os), b => b.Id == "resize-left");
}
