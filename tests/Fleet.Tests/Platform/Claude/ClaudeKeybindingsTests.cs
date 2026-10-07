using System.Text.Json;
using Fleet.Platform.Claude;
using Fleet.Platform.Claude.Models;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Shared.Keybinds.Models;

namespace Fleet.Tests.Platform.Claude;

public sealed class ClaudeKeybindingsTests : IDisposable
{
    private readonly string _config = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public void Dispose()
    {
        if (Directory.Exists(_config))
        {
            Directory.Delete(_config, recursive: true);
        }
    }

    private string KeybindingsFile => Path.Combine(_config, "keybindings.json");

    private string OwnershipFile => Path.Combine(_config, "keybindings.fleet.json");

    private static KeybindEntry Entry(string id, string action, string chord, params string[] contexts) =>
        new(
            id,
            action,
            KeybindChord.Normalize(chord),
            new Dictionary<KeybindTarget, IReadOnlyList<string>> { [KeybindTarget.Claude] = contexts },
            new Dictionary<KeybindOs, string>());

    private static IReadOnlyList<ClaudeKeybinding> Render(params KeybindEntry[] entries) =>
        ClaudeKeybindings.Render(new KeybindSet(entries), KeybindOs.Windows);

    [Fact]
    public void The_shipped_defaults_render_shift_enter_as_a_chat_newline()
    {
        var rendered = ClaudeKeybindings.Render(KeybindDefaults.Set, KeybindOs.Windows);

        Assert.Equal([new ClaudeKeybinding("Chat", "shift+enter", "chat:newline")], rendered);
    }

    [Theory]
    [InlineData("Ctrl+Shift+e", "ctrl+shift+e")]
    [InlineData("Alt+n", "alt+n")]
    [InlineData("Meta+p", "alt+p")]
    [InlineData("K", "shift+k")]
    [InlineData("Ctrl+K Ctrl+S", "ctrl+shift+k ctrl+shift+s")]
    [InlineData("Ctrl+x Ctrl+e", "ctrl+x ctrl+e")]
    [InlineData("Escape", "escape")]
    [InlineData("PageDown", "pagedown")]
    [InlineData("Super+c", "cmd+c")]
    [InlineData("Ctrl+_", "ctrl+_")]
    public void Chords_render_in_claude_keystroke_syntax(string chord, string expected) =>
        Assert.Equal(expected, Assert.Single(Render(Entry("x", "chat:stash", chord, "Chat"))).Key);

    [Theory]
    [InlineData("Ctrl+c")]
    [InlineData("Ctrl+d")]
    [InlineData("Ctrl+h")]
    [InlineData("Ctrl+m")]
    [InlineData("Ctrl+i")]
    [InlineData("Ctrl+[")]
    public void Reserved_shortcuts_are_never_rendered(string chord)
    {
        var problems = new List<string>();

        Assert.Empty(ClaudeKeybindings.Render(
            new KeybindSet([Entry("x", "chat:stash", chord, "Chat")]), KeybindOs.Windows, problems.Add));
        Assert.Contains(problems, p => p.Contains("reserved", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("F5")]
    [InlineData("Insert")]
    [InlineData("Ctrl++")]
    public void Keys_claude_cannot_name_are_skipped(string chord) =>
        Assert.Empty(Render(Entry("x", "chat:stash", chord, "Chat")));

    [Fact]
    public void A_semantic_action_without_a_claude_equivalent_is_skipped_not_invented()
    {
        var problems = new List<string>();

        var rendered = ClaudeKeybindings.Render(
            new KeybindSet([Entry("x", "resize-left", "Alt+h", "Chat")]), KeybindOs.Windows, problems.Add);

        Assert.Empty(rendered);
        Assert.Contains(problems, p => p.Contains("no Claude action", StringComparison.Ordinal));
    }

    [Fact]
    public void A_claude_action_written_as_namespace_action_passes_through() =>
        Assert.Equal(
            "app:toggleTodos",
            Assert.Single(Render(Entry("x", "app:toggleTodos", "Ctrl+t", "Global"))).Action);

    [Fact]
    public void Unknown_and_missing_contexts_are_skipped()
    {
        Assert.Empty(Render(Entry("x", "newline", "Shift+Enter", "Prompt")));
        Assert.Empty(Render(Entry("x", "newline", "Shift+Enter")));
    }

    [Fact]
    public void Every_context_of_an_entry_gets_its_own_binding()
    {
        var rendered = Render(Entry("x", "select:next", "Ctrl+n", "Select", "MessageSelector"));

        Assert.Equal(["Select", "MessageSelector"], rendered.Select(r => r.Context));
    }

    [Fact]
    public void Unbound_and_os_specific_chords_follow_the_model()
    {
        var entry = Entry("x", "newline", "Shift+Enter", "Chat") with
        {
            OsChords = new Dictionary<KeybindOs, string> { [KeybindOs.MacOs] = "Alt+Enter", [KeybindOs.Linux] = "none" },
        };
        var set = new KeybindSet([entry]);

        Assert.Equal("shift+enter", Assert.Single(ClaudeKeybindings.Render(set, KeybindOs.Windows)).Key);
        Assert.Equal("alt+enter", Assert.Single(ClaudeKeybindings.Render(set, KeybindOs.MacOs)).Key);
        Assert.Empty(ClaudeKeybindings.Render(set, KeybindOs.Linux));
    }

    [Fact]
    public void Writing_into_a_missing_file_creates_it_with_the_schema_and_a_fleet_ownership_record()
    {
        var result = new ClaudeKeybindingsWriter(_config).Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.Changed);
        Assert.Empty(result.Value.Conflicts);

        using var doc = JsonDocument.Parse(File.ReadAllText(KeybindingsFile));
        Assert.Equal(ClaudeKeybindings.Schema, doc.RootElement.GetProperty("$schema").GetString());
        var block = Assert.Single(doc.RootElement.GetProperty("bindings").EnumerateArray());
        Assert.Equal("Chat", block.GetProperty("context").GetString());
        Assert.Equal("chat:newline", block.GetProperty("bindings").GetProperty("shift+enter").GetString());
        Assert.Contains("shift+enter", File.ReadAllText(OwnershipFile), StringComparison.Ordinal);
    }

    [Fact]
    public void The_users_own_bindings_unbinds_and_extra_fields_survive()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(KeybindingsFile, """
            {
              "bindings": [
                { "context": "Chat", "bindings": { "ctrl+e": "chat:externalEditor", "ctrl+s": null } },
                { "context": "Global", "bindings": { "ctrl+t": "app:toggleTodos" } }
              ],
              "mine": 1
            }
            """);

        new ClaudeKeybindingsWriter(_config).Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));

        using var doc = JsonDocument.Parse(File.ReadAllText(KeybindingsFile));
        var blocks = doc.RootElement.GetProperty("bindings").EnumerateArray().ToList();
        var chat = blocks[0].GetProperty("bindings");
        Assert.Equal("chat:externalEditor", chat.GetProperty("ctrl+e").GetString());
        Assert.Equal(JsonValueKind.Null, chat.GetProperty("ctrl+s").ValueKind);
        Assert.Equal("chat:newline", chat.GetProperty("shift+enter").GetString());
        Assert.Equal("app:toggleTodos", blocks[1].GetProperty("bindings").GetProperty("ctrl+t").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("mine").GetInt32());
        Assert.False(doc.RootElement.TryGetProperty("$schema", out _));
    }

    [Theory]
    [InlineData("\"Shift+Enter\": \"chat:submit\"")]
    [InlineData("\"shift+enter\": null")]
    public void A_key_the_user_already_binds_is_a_conflict_and_is_left_alone(string userBinding)
    {
        Directory.CreateDirectory(_config);
        var before = $$"""{ "bindings": [ { "context": "Chat", "bindings": { {{userBinding}} } } ] }""";
        File.WriteAllText(KeybindingsFile, before);

        var result = new ClaudeKeybindingsWriter(_config).Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));

        Assert.Single(result.Value!.Conflicts);
        Assert.False(File.Exists(OwnershipFile));
        using var doc = JsonDocument.Parse(File.ReadAllText(KeybindingsFile));
        var chat = Assert.Single(doc.RootElement.GetProperty("bindings").EnumerateArray()).GetProperty("bindings");
        Assert.Single(chat.EnumerateObject());
    }

    [Fact]
    public void A_user_binding_identical_to_fleets_is_neither_a_conflict_nor_taken_over()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(
            KeybindingsFile,
            """{ "bindings": [ { "context": "Chat", "bindings": { "shift+enter": "chat:newline" } } ] }""");
        var writer = new ClaudeKeybindingsWriter(_config);

        var result = writer.Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));

        Assert.Empty(result.Value!.Conflicts);
        Assert.False(File.Exists(OwnershipFile));

        writer.Write([]);
        Assert.Contains("chat:newline", File.ReadAllText(KeybindingsFile), StringComparison.Ordinal);
    }

    [Fact]
    public void Fleet_entries_that_left_the_model_are_removed_and_their_empty_block_with_them()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(
            KeybindingsFile, """{ "bindings": [ { "context": "Global", "bindings": { "ctrl+t": "app:toggleTodos" } } ] }""");
        var writer = new ClaudeKeybindingsWriter(_config);
        writer.Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));

        var result = writer.Write([]);

        Assert.True(result.Value!.Changed);
        Assert.False(File.Exists(OwnershipFile));
        using var doc = JsonDocument.Parse(File.ReadAllText(KeybindingsFile));
        var block = Assert.Single(doc.RootElement.GetProperty("bindings").EnumerateArray());
        Assert.Equal("Global", block.GetProperty("context").GetString());
    }

    [Fact]
    public void A_rechorded_fleet_entry_moves_to_its_new_key()
    {
        var writer = new ClaudeKeybindingsWriter(_config);
        writer.Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));

        writer.Write(Render(Entry("x", "newline", "Alt+Enter", "Chat")));

        using var doc = JsonDocument.Parse(File.ReadAllText(KeybindingsFile));
        var chat = Assert.Single(doc.RootElement.GetProperty("bindings").EnumerateArray()).GetProperty("bindings");
        Assert.Equal("alt+enter", Assert.Single(chat.EnumerateObject()).Name);
    }

    [Fact]
    public void A_fleet_entry_the_user_changed_becomes_theirs_and_is_kept()
    {
        var writer = new ClaudeKeybindingsWriter(_config);
        writer.Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));
        File.WriteAllText(
            KeybindingsFile,
            File.ReadAllText(KeybindingsFile).Replace("chat:newline", "chat:submit", StringComparison.Ordinal));

        var result = writer.Write([]);

        Assert.True(result.Succeeded);
        Assert.Contains("chat:submit", File.ReadAllText(KeybindingsFile), StringComparison.Ordinal);
        Assert.False(File.Exists(OwnershipFile));
    }

    [Fact]
    public void Writing_the_same_model_twice_changes_nothing_the_second_time()
    {
        var writer = new ClaudeKeybindingsWriter(_config);
        var wanted = Render(Entry("x", "newline", "Shift+Enter", "Chat"));
        writer.Write(wanted);
        var before = File.ReadAllText(KeybindingsFile);

        Assert.False(writer.Write(wanted).Value!.Changed);
        Assert.Equal(before, File.ReadAllText(KeybindingsFile));
    }

    [Fact]
    public void Nothing_to_write_and_no_file_creates_nothing()
    {
        var result = new ClaudeKeybindingsWriter(_config).Write([]);

        Assert.False(result.Value!.Changed);
        Assert.False(File.Exists(KeybindingsFile));
        Assert.False(File.Exists(OwnershipFile));
    }

    [Theory]
    [InlineData("""{ "bindings": null }""")]
    [InlineData("""{ "bindings": [ null ] }""")]
    [InlineData("""{ "bindings": [ { "context": "Chat", "bindings": null } ] }""")]
    public void A_file_with_nulls_where_structure_belongs_is_left_untouched(string json)
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(KeybindingsFile, json);

        var result = new ClaudeKeybindingsWriter(_config).Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));

        Assert.False(result.Succeeded);
        Assert.Equal(json, File.ReadAllText(KeybindingsFile));
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{ "owned": [ null ] }""")]
    [InlineData("""{ "owned": [ { "context": "Chat" } ] }""")]
    public void A_broken_ownership_record_fails_instead_of_forgetting_what_fleet_owns(string sidecar)
    {
        var writer = new ClaudeKeybindingsWriter(_config);
        writer.Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));
        var before = File.ReadAllText(KeybindingsFile);
        File.WriteAllText(OwnershipFile, sidecar);

        var result = writer.Write([]);

        Assert.False(result.Succeeded);
        Assert.Equal(before, File.ReadAllText(KeybindingsFile));
        Assert.Equal(sidecar, File.ReadAllText(OwnershipFile));
    }

    [Fact]
    public void A_user_binding_in_a_second_block_of_the_same_context_is_a_conflict()
    {
        Directory.CreateDirectory(_config);
        var before = """
            { "bindings": [
              { "context": "Chat", "bindings": { "ctrl+e": "chat:externalEditor" } },
              { "context": "Chat", "bindings": { "shift+enter": null } }
            ] }
            """;
        File.WriteAllText(KeybindingsFile, before);

        var result = new ClaudeKeybindingsWriter(_config).Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));

        Assert.Single(result.Value!.Conflicts);
        Assert.Equal(before, File.ReadAllText(KeybindingsFile));
    }

    [Fact]
    public void A_users_file_is_not_reformatted_when_fleet_changes_nothing()
    {
        Directory.CreateDirectory(_config);
        var before = """{"bindings":[{"context":"Chat","bindings":{"ctrl+e":"chat:externalEditor"}}]}""";
        File.WriteAllText(KeybindingsFile, before);

        var result = new ClaudeKeybindingsWriter(_config).Write([]);

        Assert.False(result.Value!.Changed);
        Assert.Equal(before, File.ReadAllText(KeybindingsFile));
    }

    [Theory]
    [InlineData("command+k", "Super+k")]
    [InlineData("Shift+Enter", "shift+enter")]
    [InlineData("meta+p", "alt+p")]
    [InlineData("ctrl+x ctrl+e", "Ctrl+x Ctrl+e")]
    public void Keys_compare_the_way_claude_reads_them(string left, string right) =>
        Assert.True(ClaudeKeybindings.SameKey(left, right));

    [Fact]
    public void A_file_that_is_not_json_is_left_untouched()
    {
        Directory.CreateDirectory(_config);
        File.WriteAllText(KeybindingsFile, "{ not json");

        var result = new ClaudeKeybindingsWriter(_config).Write(Render(Entry("x", "newline", "Shift+Enter", "Chat")));

        Assert.False(result.Succeeded);
        Assert.Equal("{ not json", File.ReadAllText(KeybindingsFile));
    }
}
