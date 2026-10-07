using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Fleet.Platform.Claude.Models;
using Fleet.Platform.Storage;
using Fleet.Shared.Results;

namespace Fleet.Platform.Claude;

public sealed class ClaudeKeybindingsWriter(string configDirectory)
{
    public string KeybindingsFile => ClaudeKeybindings.PathIn(configDirectory);

    public string OwnershipFile => ClaudeKeybindings.OwnershipPathIn(configDirectory);

    public Result<ClaudeKeybindingsWritten> Write(IReadOnlyList<ClaudeKeybinding> wanted)
    {
        var existingText = ReadText(KeybindingsFile);

        if (existingText is null)
        {
            return Result<ClaudeKeybindingsWritten>.Fail($"fleet could not read {KeybindingsFile}.");
        }

        var file = Parse(existingText, ClaudeJsonContext.Default.ClaudeKeybindingsFile, NewFile);

        if (file is null)
        {
            return Result<ClaudeKeybindingsWritten>.Fail(
                $"{KeybindingsFile} could not be read as JSON; fleet left it untouched.");
        }

        var ownershipText = ReadText(OwnershipFile) ?? string.Empty;
        var ownership = Parse(
            ownershipText, ClaudeJsonContext.Default.ClaudeKeybindingsOwnership, () => new ClaudeKeybindingsOwnership())
            ?? new ClaudeKeybindingsOwnership();

        RemoveOwned(file, ownership.Owned);

        var owned = new List<ClaudeKeybinding>();
        var conflicts = new List<ClaudeKeybinding>();

        foreach (var binding in wanted)
        {
            var block = file.Bindings.FirstOrDefault(b => b.Context == binding.Context);
            var taken = block?.Bindings.Keys.FirstOrDefault(k => ClaudeKeybindings.SameKey(k, binding.Key));

            if (taken is not null)
            {
                if (block!.Bindings[taken] != binding.Action)
                {
                    conflicts.Add(binding);
                }

                continue;
            }

            if (block is null)
            {
                block = new ClaudeKeybindingBlock { Context = binding.Context };
                file.Bindings.Add(block);
            }

            block.Bindings[binding.Key] = binding.Action;
            owned.Add(binding);
        }

        var changed = false;

        if (wanted.Count > 0 || existingText.Length > 0)
        {
            var text = JsonSerializer.Serialize(file, Readable.ClaudeKeybindingsFile)
                + Environment.NewLine;

            if (text != existingText)
            {
                if (!Replace(KeybindingsFile, text))
                {
                    return Result<ClaudeKeybindingsWritten>.Fail($"fleet could not write {KeybindingsFile}.");
                }

                changed = true;
            }
        }

        if (!WriteOwnership(owned, ownershipText))
        {
            return Result<ClaudeKeybindingsWritten>.Fail($"fleet could not write {OwnershipFile}.");
        }

        return Result<ClaudeKeybindingsWritten>.Ok(new ClaudeKeybindingsWritten(changed, conflicts));
    }

    private static readonly ClaudeJsonContext Readable = new(
        new JsonSerializerOptions(ClaudeJsonContext.Default.Options)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    private static ClaudeKeybindingsFile NewFile() =>
        new() { Schema = ClaudeKeybindings.Schema, Docs = ClaudeKeybindings.Docs };

    private static void RemoveOwned(ClaudeKeybindingsFile file, IReadOnlyList<ClaudeKeybinding> owned)
    {
        foreach (var binding in owned)
        {
            var block = file.Bindings.FirstOrDefault(b => b.Context == binding.Context);
            var key = block?.Bindings.Keys.FirstOrDefault(k => ClaudeKeybindings.SameKey(k, binding.Key));

            if (key is null || block!.Bindings[key] != binding.Action)
            {
                continue;
            }

            block.Bindings.Remove(key);

            if (block.Bindings.Count == 0 && block.Extra is null or { Count: 0 })
            {
                file.Bindings.Remove(block);
            }
        }
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

    private static T? Parse<T>(string text, JsonTypeInfo<T> info, Func<T> empty)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return empty();
        }

        try
        {
            return JsonSerializer.Deserialize(text, info) ?? empty();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool Replace(string path, string content) =>
        BusyFiles.Replace(path, temp => File.WriteAllText(temp, content));
}
