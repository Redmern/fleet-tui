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
        ["w"] = "next-workspace",
        ["space"] = "menu",
        ["["] = "copy-mode",
        ["]"] = "paste",
        ["f"] = "float-new",
        ["t"] = "float-toggle",
        ["e"] = "float-embed",
        ["g"] = "float-mode",
        ["r"] = "reload",
        ["d"] = "detach",
        ["q"] = "detach",
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

    private MuxKeys(KeyChord prefix, string prefixSpec, List<Binding> prefixKeys, List<Binding> directKeys)
    {
        Prefix = prefix;
        PrefixSpec = prefixSpec;
        PrefixKeys = prefixKeys;
        DirectKeys = directKeys;
    }

    public KeyChord Prefix { get; }

    public string PrefixSpec { get; }

    public IReadOnlyList<Binding> PrefixKeys { get; }

    public IReadOnlyList<Binding> DirectKeys { get; }

    public static MuxKeys Defaults => From(null, null);

    public static MuxKeys From(MuxKeysFile? file, string? prefixOverride)
    {
        var prefixSpec = prefixOverride ?? file?.Prefix ?? DefaultPrefix;
        var prefix = KeyChord.Parse(prefixSpec) ?? throw new FormatException("the prefix is empty");

        return new MuxKeys(
            prefix,
            prefixSpec,
            Bindings(DefaultPrefixKeys, file?.PrefixKeys),
            Bindings(DefaultDirectKeys, file?.Keys));
    }

    public static MuxKeys Load(string path, string? prefixOverride, Action<string> log)
    {
        try
        {
            var file = File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), MuxKeysJsonContext.Default.MuxKeysFile)
                : null;
            return From(file, prefixOverride);
        }
        catch (Exception e) when (e is IOException or JsonException or FormatException or UnauthorizedAccessException)
        {
            log($"keys: {path} not used ({e.Message}); using the defaults");
            return From(null, prefixOverride);
        }
    }

    public string? PrefixCommand(Key key, Mods mods, string? text) =>
        PrefixKeys.FirstOrDefault(b => b.Chord.Matches(key, mods, text))?.Command;

    public string? DirectCommand(Key key, Mods mods, string? text) =>
        DirectKeys.FirstOrDefault(b => b.Chord.Matches(key, mods, text))?.Command;

    public (string? Command, int Length) PrefixBytes(ReadOnlySpan<byte> bytes) => Longest(PrefixKeys, bytes);

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

    private static List<Binding> Bindings(IReadOnlyDictionary<string, string> defaults, Dictionary<string, string>? overrides)
    {
        var merged = new Dictionary<string, string>(defaults, StringComparer.Ordinal);

        foreach (var (spec, command) in overrides ?? [])
        {
            merged[spec] = command;
        }

        return merged
            .Where(kv => !string.Equals(kv.Value, Unbound, StringComparison.OrdinalIgnoreCase) && kv.Value.Length > 0)
            .Select(kv => KeyChord.Parse(kv.Key) is { } chord ? new Binding(kv.Key, chord, kv.Value.Trim(), chord.Bytes()) : null)
            .OfType<Binding>()
            .ToList();
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
}

[JsonSourceGenerationOptions(ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(MuxKeysFile))]
public sealed partial class MuxKeysJsonContext : JsonSerializerContext;
