using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Fleet.Shared.Results;

namespace Fleet.Platform.Pi;

public static class PiTrustStore
{
    private static readonly JsonWriterOptions AsPiWritesIt = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static Result<string> WithTrusted(string text, string folder)
    {
        var read = Read(text);

        if (!read.Succeeded)
        {
            return Result<string>.Fail(read.Error ?? string.Empty);
        }

        var decisions = read.Value;
        var decided = Nearest(decisions, folder);

        if (decided == true)
        {
            return Result<string>.Ok(text);
        }

        if (decided == false)
        {
            return Result<string>.Fail("pi has a saved decision to not trust this folder and fleet does not change it");
        }

        foreach (var cleared in decisions.Keys.Where(key => Same(key, folder)).ToList())
        {
            decisions.Remove(cleared);
        }

        decisions[folder] = true;

        return Result<string>.Ok(Written(decisions));
    }

    public static string Canonical(string folder)
    {
        var full = Path.GetFullPath(folder);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root;

        foreach (var part in full[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = Resolved(Path.Combine(current, part));
        }

        return current;
    }

    private static string Resolved(string path)
    {
        try
        {
            var target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true);

            return target is null ? path : Canonical(target.FullName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static Result<Dictionary<string, bool?>> Read(string text)
    {
        var decisions = new Dictionary<string, bool?>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(text))
        {
            return Result<Dictionary<string, bool?>>.Ok(decisions);
        }

        try
        {
            using var document = JsonDocument.Parse(text.TrimStart('﻿'));

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Result<Dictionary<string, bool?>>.Fail("it is not a JSON object");
            }

            foreach (var entry in document.RootElement.EnumerateObject())
            {
                if (entry.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                {
                    return Result<Dictionary<string, bool?>>.Fail(
                        $"the value for \"{entry.Name}\" is not true, false or null");
                }

                decisions[entry.Name] = entry.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : entry.Value.GetBoolean();
            }
        }
        catch (JsonException)
        {
            return Result<Dictionary<string, bool?>>.Fail("it is not JSON");
        }

        return Result<Dictionary<string, bool?>>.Ok(decisions);
    }

    private static bool? Nearest(Dictionary<string, bool?> decisions, string folder)
    {
        for (var current = folder; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            var decision = decisions
                .Where(entry => entry.Value is not null && Same(entry.Key, current))
                .Select(entry => entry.Value)
                .FirstOrDefault();

            if (decision is not null)
            {
                return decision;
            }
        }

        return null;
    }

    private static bool Same(string key, string folder)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(key)),
                Path.TrimEndingDirectorySeparator(folder),
                PathComparison);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string Written(Dictionary<string, bool?> decisions)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream, AsPiWritesIt))
        {
            writer.WriteStartObject();

            foreach (var (path, decision) in decisions.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                if (decision is null)
                {
                    writer.WriteNull(path);
                }
                else
                {
                    writer.WriteBoolean(path, decision.Value);
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }
}
