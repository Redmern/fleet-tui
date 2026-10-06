using System.Text.Json;
using System.Text.Json.Serialization;
using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Platform.Mux.Embedded.Input;

public sealed class MuxKeys
{
    public const string FileName = "embedded-keys.json";

    public const string Unbound = "none";

    public static readonly IReadOnlyDictionary<string, string> DefaultPrefixKeys = new Dictionary<string, string>
    {
        ["h"] = "focus-left",
        ["j"] = "focus-down",
        ["k"] = "focus-up",
        ["l"] = "focus-right",
        ["left"] = "resize left",
        ["right"] = "resize right",
        ["up"] = "resize up",
        ["down"] = "resize down",
        ["%"] = "split-right",
        ["\""] = "split-down",
        ["c"] = "new-tab",
        ["n"] = "next-tab",
        ["p"] = "prev-tab",
        ["1"] = "tab 1",
        ["2"] = "tab 2",
        ["3"] = "tab 3",
        ["4"] = "tab 4",
        ["5"] = "tab 5",
        ["6"] = "tab 6",
        ["7"] = "tab 7",
        ["8"] = "tab 8",
        ["9"] = "tab 9",
        ["x"] = "kill-pane",
        ["&"] = "kill-tab",
        ["z"] = "zoom",
        ["o"] = "next-pane",
        ["s"] = "switch-project",
        ["space"] = "menu",
        ["["] = "copy-mode",
        ["]"] = "paste",
        ["d"] = "detach",
        ["f f"] = "float-new",
        ["f t"] = "float-toggle",
        ["f e"] = "float-embed",
        ["f g"] = "float-mode",
        ["w w"] = "next-workspace",
        ["w s"] = "switch-project",
        ["q d"] = "detach",
        ["q q"] = "detach",
        ["q r"] = "reload",
    };

    public static readonly IReadOnlyDictionary<string, string> DefaultGroups = new Dictionary<string, string>
    {
        ["f"] = "float",
        ["w"] = "project",
        ["q"] = "session",
    };

    public static readonly IReadOnlyDictionary<string, string> DefaultDirectKeys = new Dictionary<string, string>
    {
        ["ctrl+h"] = "smart-focus left",
        ["ctrl+j"] = "smart-focus down",
        ["ctrl+k"] = "smart-focus up",
        ["ctrl+l"] = "smart-focus right",
        ["alt+h"] = "smart-focus left",
        ["alt+j"] = "smart-focus down",
        ["alt+k"] = "smart-focus up",
        ["alt+l"] = "smart-focus right",
        ["alt+left"] = "prev-tab",
        ["alt+right"] = "next-tab",
        ["ctrl+tab"] = "next-tab",
        ["ctrl+shift+tab"] = "prev-tab",
        ["ctrl+enter"] = "menu",
        ["shift+enter"] = "newline",
    };

    public const string DefaultPrefix = "ctrl+s";

    private MuxKeys(KeyChord prefix, string prefixSpec, KeyNode root, List<Binding> directKeys)
    {
        Prefix = prefix;
        PrefixSpec = prefixSpec;
        Root = root;
        PrefixKeys = root.All().ToList();
        DirectKeys = directKeys;
    }

    public KeyChord Prefix { get; }

    public string PrefixSpec { get; }

    public KeyNode Root { get; }

    public IReadOnlyList<Binding> PrefixKeys { get; }

    public IReadOnlyList<Binding> DirectKeys { get; }

    public static MuxKeys Defaults => From(null, null);

    public static MuxKeys From(
        MuxKeysFile? file,
        string? prefixOverride,
        IReadOnlyDictionary<string, string>? extraDirect = null,
        Action<string>? log = null)
    {
        var prefixSpec = prefixOverride ?? file?.Prefix ?? DefaultPrefix;
        var prefix = KeyChord.Parse(prefixSpec) ?? throw new FormatException("the prefix is empty");
        log ??= _ => { };

        return new MuxKeys(
            prefix,
            prefixSpec,
            Tree(prefix.Label, file?.PrefixKeys, file?.Groups, log),
            Bindings(WithExtra(DefaultDirectKeys, extraDirect), file?.Keys, log));
    }

    public static MuxKeys Load(
        string path,
        string? prefixOverride,
        Action<string> log,
        IReadOnlyDictionary<string, string>? extraDirect = null)
    {
        try
        {
            var file = File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), MuxKeysJsonContext.Default.MuxKeysFile)
                : null;
            return From(file, prefixOverride, extraDirect, log);
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or UnauthorizedAccessException)
        {
            log($"keys: {path} not used ({e.Message}); using the defaults");
            return From(null, prefixOverride, extraDirect, log);
        }
    }

    public string? PrefixCommand(Key key, Mods mods, string? text) =>
        Root.Match(key, mods, text) is KeyStep.Run run ? run.Command : null;

    public string? DirectCommand(Key key, Mods mods, string? text) =>
        DirectKeys.FirstOrDefault(b => b.Chord.Matches(key, mods, text))?.Command;

    public (string? Command, int Length) PrefixBytes(ReadOnlySpan<byte> bytes) =>
        Root.Match(bytes) is (KeyStep.Run run, var length) ? (run.Command, length) : (null, 0);

    public (string? Command, int Length) DirectBytes(ReadOnlySpan<byte> bytes) => Longest(DirectKeys, bytes);

    private static (string? Command, int Length) Longest(IReadOnlyList<Binding> bindings, ReadOnlySpan<byte> bytes)
    {
        string? command = null;
        var length = 0;

        foreach (var binding in bindings)
        {
            if (binding.Bytes is { Length: > 0 } sequence && sequence.Length > length && bytes.StartsWith(sequence))
            {
                command = binding.Command;
                length = sequence.Length;
            }
        }

        return (command, length);
    }

    private static Dictionary<string, string> WithExtra(
        IReadOnlyDictionary<string, string> defaults, IReadOnlyDictionary<string, string>? extra)
    {
        var merged = new Dictionary<string, string>(defaults, StringComparer.Ordinal);

        foreach (var (spec, command) in extra ?? new Dictionary<string, string>())
        {
            if (Parses(spec))
            {
                merged[spec] = command;
            }
        }

        return merged;
    }

    private static bool Parses(string spec)
    {
        try
        {
            return KeyChord.Parse(spec) is not null;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsUnbound(string command) =>
        command.Trim().Length == 0 || string.Equals(command.Trim(), Unbound, StringComparison.OrdinalIgnoreCase);

    private static List<Binding> Bindings(
        IReadOnlyDictionary<string, string> defaults, Dictionary<string, string>? overrides, Action<string> log)
    {
        var merged = new Dictionary<string, string>(defaults, StringComparer.Ordinal);

        foreach (var (spec, command) in overrides ?? [])
        {
            if (spec.Trim().Contains(' '))
            {
                log($"keys: \"{spec}\" ignored, key sequences only work after the prefix");
                continue;
            }

            merged[spec] = command;
        }

        return merged
            .Where(kv => !IsUnbound(kv.Value))
            .Select(kv => KeyChord.Parse(kv.Key) is { } chord ? new Binding(kv.Key, chord, kv.Value.Trim(), chord.Bytes()) : null)
            .OfType<Binding>()
            .ToList();
    }

    private static KeyNode Tree(
        string prefixLabel, Dictionary<string, string>? overrides, Dictionary<string, string>? groupOverrides, Action<string> log)
    {
        var specs = new Dictionary<string, Entry>(StringComparer.Ordinal);

        foreach (var (spec, command) in DefaultPrefixKeys)
        {
            specs[spec] = new Entry(spec, command, false);
        }

        foreach (var (spec, command) in overrides ?? [])
        {
            specs[spec] = new Entry(spec, command, true);
        }

        var labels = new Dictionary<string, string>(DefaultGroups, StringComparer.Ordinal);
        foreach (var (spec, label) in groupOverrides ?? [])
        {
            labels[spec] = label;
        }

        var groups = labels
            .Select(kv => (Path: Sequence(kv.Key).Chords, Label: kv.Value.Trim()))
            .Where(g => g.Path.Length > 0)
            .ToList();
        var dropped = groups.Where(g => IsUnbound(g.Label)).Select(g => g.Path).ToList();

        var live = specs.Values
            .Where(e => !IsUnbound(e.Command) && e.Chords.Length > 0)
            .Where(e => !dropped.Any(path => e.Chords.Length > path.Length && e.Chords.AsSpan().StartsWith(path)))
            .ToList();
        var kept = live.Where(e => !live.Any(other => Beats(other, e, log))).ToList();

        var root = new KeyNode(prefixLabel);
        foreach (var entry in kept)
        {
            var node = root;
            for (var i = 0; i < entry.Chords.Length - 1; i++)
            {
                var path = entry.Chords[..(i + 1)];
                var label = groups.FirstOrDefault(g => g.Path.AsSpan().SequenceEqual(path)).Label;
                node = node.Child(entry.Tokens[i], entry.Chords[i], label is { Length: > 0 } ? label : entry.Chords[i].Label);
            }

            var last = entry.Chords[^1];
            node.Add(new Binding(entry.Spec, last, entry.Command.Trim(), last.Bytes()));
        }

        return root;
    }

    private static bool Beats(Entry other, Entry entry, Action<string> log)
    {
        if (ReferenceEquals(other, entry) || other.Chords.Length == entry.Chords.Length)
        {
            return false;
        }

        var (leaf, sequence) = other.Chords.Length < entry.Chords.Length ? (other, entry) : (entry, other);
        if (!sequence.Chords.AsSpan().StartsWith(leaf.Chords))
        {
            return false;
        }

        var winner = leaf.User == sequence.User || leaf.User ? leaf : sequence;
        if (!ReferenceEquals(winner, other))
        {
            return false;
        }

        if (leaf.User && sequence.User)
        {
            log($"keys: \"{sequence.Spec}\" ignored, \"{leaf.Spec}\" is already bound");
        }

        return true;
    }

    private static (string[] Tokens, KeyChord[] Chords) Sequence(string spec)
    {
        var tokens = spec.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var chords = tokens.Select(t => KeyChord.Parse(t) ?? throw new FormatException($"empty key in '{spec}'")).ToArray();
        return (tokens, chords);
    }

    private sealed class Entry
    {
        public Entry(string spec, string command, bool user)
        {
            Spec = spec;
            Command = command;
            User = user;
            (Tokens, Chords) = Sequence(spec);
        }

        public string Spec { get; }

        public string Command { get; }

        public bool User { get; }

        public string[] Tokens { get; }

        public KeyChord[] Chords { get; }
    }

    public sealed record Binding(string Spec, KeyChord Chord, string Command, byte[]? Bytes)
    {
        public (string Name, string? Arg) Split()
        {
            var space = Command.IndexOf(' ');
            return space < 0 ? (Command, null) : (Command[..space], Command[(space + 1)..].Trim());
        }
    }
}

public sealed class MuxKeysFile
{
    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    [JsonPropertyName("prefixKeys")]
    public Dictionary<string, string>? PrefixKeys { get; set; }

    [JsonPropertyName("keys")]
    public Dictionary<string, string>? Keys { get; set; }

    [JsonPropertyName("groups")]
    public Dictionary<string, string>? Groups { get; set; }
}

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(MuxKeysFile))]
public sealed partial class MuxKeysJsonContext : JsonSerializerContext;
