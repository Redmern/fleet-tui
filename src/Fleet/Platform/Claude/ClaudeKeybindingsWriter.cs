using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Fleet.Platform.Claude.Models;
using Fleet.Platform.Storage;
using Fleet.Shared.Results;

namespace Fleet.Platform.Claude;

public sealed class ClaudeKeybindingsWriter(string configDirectory)
{
    private static readonly ClaudeJsonContext Readable = new(
        new JsonSerializerOptions(ClaudeJsonContext.Default.Options)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    public string KeybindingsFile => ClaudeKeybindings.PathIn(configDirectory);

    public string OwnershipFile => ClaudeKeybindings.OwnershipPathIn(configDirectory);

    public Result<ClaudeKeybindingsWritten> Write(IReadOnlyList<ClaudeKeybinding> wanted)
    {
        var existingText = ReadText(KeybindingsFile);
        var file = existingText is null
            ? null
            : Parse(existingText, ClaudeJsonContext.Default.ClaudeKeybindingsFile, NewFile, IsValid);

        if (existingText is null || file is null)
        {
            return Fail(KeybindingsFile);
        }

        var ownershipText = ReadText(OwnershipFile);
        var ownership = ownershipText is null
            ? null
            : Parse(ownershipText, ClaudeJsonContext.Default.ClaudeKeybindingsOwnership, () => new ClaudeKeybindingsOwnership(), IsValid);

        if (ownershipText is null || ownership is null)
        {
            return Fail(OwnershipFile);
        }

        var changed = RemoveOwned(file, ownership.Owned);
        var owned = new List<ClaudeKeybinding>();
        var conflicts = new List<ClaudeKeybinding>();

        foreach (var binding in wanted)
        {
            var taken = Matches(file, binding).ToList();

            if (taken.Count > 0)
            {
                if (taken.Any(t => t.Block.Bindings[t.Key] != binding.Action))
                {
                    conflicts.Add(binding);
                }

                continue;
            }

            var block = file.Bindings.FirstOrDefault(b => b.Context == binding.Context);

            if (block is null)
            {
                block = new ClaudeKeybindingBlock { Context = binding.Context };
                file.Bindings.Add(block);
            }

            block.Bindings[binding.Key] = binding.Action;
            owned.Add(binding);
            changed = true;
        }

        var text = changed
            ? JsonSerializer.Serialize(file, Readable.ClaudeKeybindingsFile) + Environment.NewLine
            : existingText;

        if (text == existingText)
        {
            return WriteOwnership(owned, ownershipText)
                ? Result<ClaudeKeybindingsWritten>.Ok(new ClaudeKeybindingsWritten(false, conflicts))
                : Fail(OwnershipFile, "write");
        }

        if (!WriteOwnership([.. ownership.Owned.Union(owned)], ownershipText))
        {
            return Fail(OwnershipFile, "write");
        }

        if (!Replace(KeybindingsFile, text))
        {
            return Fail(KeybindingsFile, "write");
        }

        return WriteOwnership(owned, ReadText(OwnershipFile) ?? string.Empty)
            ? Result<ClaudeKeybindingsWritten>.Ok(new ClaudeKeybindingsWritten(true, conflicts))
            : Fail(OwnershipFile, "write");
    }

    private static Result<ClaudeKeybindingsWritten> Fail(string path) =>
        Result<ClaudeKeybindingsWritten>.Fail($"{path} could not be read as JSON; fleet left it untouched.");

    private static Result<ClaudeKeybindingsWritten> Fail(string path, string verb) =>
        Result<ClaudeKeybindingsWritten>.Fail($"fleet could not {verb} {path}.");

    private static ClaudeKeybindingsFile NewFile() =>
        new() { Schema = ClaudeKeybindings.Schema, Docs = ClaudeKeybindings.Docs };

    private static bool IsValid(ClaudeKeybindingsFile file) =>
        file.Bindings is not null
        && file.Bindings.All(b => b is { Context: not null, Bindings: not null });

    private static bool IsValid(ClaudeKeybindingsOwnership ownership) =>
        ownership.Owned is not null
        && ownership.Owned.All(o => o is { Context: not null, Key: not null, Action: not null });

    private static IEnumerable<(ClaudeKeybindingBlock Block, string Key)> Matches(
        ClaudeKeybindingsFile file, ClaudeKeybinding binding) =>
        file.Bindings
            .Where(b => b.Context == binding.Context)
            .SelectMany(b => b.Bindings.Keys
                .Where(k => ClaudeKeybindings.SameKey(k, binding.Key))
                .Select(k => (b, k)));

    private static bool RemoveOwned(ClaudeKeybindingsFile file, IReadOnlyList<ClaudeKeybinding> owned)
    {
        var removed = false;

        foreach (var binding in owned)
        {
            foreach (var (block, key) in Matches(file, binding).ToList())
            {
                if (block.Bindings[key] != binding.Action)
                {
                    continue;
                }

                block.Bindings.Remove(key);
                removed = true;

                if (block.Bindings.Count == 0 && block.Extra is null or { Count: 0 })
                {
                    file.Bindings.Remove(block);
                }
            }
        }

        return removed;
    }

    private bool WriteOwnership(IReadOnlyList<ClaudeKeybinding> owned, string existingText)
    {
        if (owned.Count == 0)
        {
            return existingText.Length == 0 || BusyFiles.Retry(() => Delete(OwnershipFile), BusyFiles.Patience) is not null;
        }

        var text = JsonSerializer.Serialize(
            new ClaudeKeybindingsOwnership { Owned = [.. owned] },
            Readable.ClaudeKeybindingsOwnership) + Environment.NewLine;

        return text == existingText || Replace(OwnershipFile, text);
    }

    private static string Delete(string path)
    {
        File.Delete(path);
        return path;
    }

    private static string? ReadText(string path) =>
        BusyFiles.Retry(() => File.Exists(path) ? File.ReadAllText(path) : string.Empty, BusyFiles.Patience);

    private static T? Parse<T>(string text, JsonTypeInfo<T> info, Func<T> empty, Func<T, bool> valid)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return empty();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(text, info) ?? empty();
            return valid(parsed) ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Replace(string path, string content) =>
        BusyFiles.Replace(path, temp => File.WriteAllText(temp, content));
}
