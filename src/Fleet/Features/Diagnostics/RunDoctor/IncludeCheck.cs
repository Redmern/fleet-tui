using System.Text.Json;
using System.Text.RegularExpressions;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Shared;
using Fleet.Shared.Harness;
using Fleet.Shared.Orchestrations;

namespace Fleet.Features.Diagnostics.RunDoctor;

public static partial class IncludeCheck
{
    private const string Indent = "                ";

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<string> Lines(IncludeFacts facts)
    {
        var include = Show(facts, facts.IncludePath);
        var state = facts.IncludeText switch
        {
            null => "missing; run fleet setup",
            var text when text == OrchestratorInclude.Text => "current",
            _ => "out of date; run fleet setup",
        };

        List<string> lines = [$"  orchestrator  {include}, {state}"];

        lines.AddRange(Claude(facts, include));

        if (facts.HarnessesInUse.Contains(HarnessRegistry.OpencodeName))
        {
            lines.AddRange(Opencode(facts, include));
        }

        if (facts.HarnessesInUse.Contains(HarnessRegistry.CodexName))
        {
            lines.Add(CannotImport(facts, HarnessRegistry.CodexName, facts.CodexFile));
        }

        if (facts.HarnessesInUse.Contains(HarnessRegistry.PiName))
        {
            lines.Add(CannotImport(facts, HarnessRegistry.PiName, facts.PiFile));
        }

        return lines;
    }

    private static IEnumerable<string> Claude(IncludeFacts facts, string include)
    {
        var importing = facts.ClaudeFiles.FirstOrDefault(f => f.Text is { } text && Imports(facts, text));

        if (importing is not null)
        {
            return [$"{Indent}claude: imported by {Show(facts, importing.Path)}"];
        }

        return
        [
            $"{Indent}claude: not imported, so Claude orchestrators do not read it",
            $"  add to ~/CLAUDE.md:  @{include}",
        ];
    }

    private static IEnumerable<string> Opencode(IncludeFacts facts, string include)
    {
        var config = Show(facts, facts.OpencodeConfigs[0].Path);
        var read = facts.OpencodeConfigs.Select(f => (File: f, Read: Listed(facts, f))).ToList();

        if (read.FirstOrDefault(r => r.Read.Listed) is { File: { } listing })
        {
            return [$"{Indent}opencode: listed in {Show(facts, listing.Path)} instructions"];
        }

        var problem = string.Concat(read.Select(r => r.Read.Problem));

        return
        [
            $"{Indent}opencode: AGENTS.md cannot import a file, so list it in {config} instead{problem}",
            $"  add to \"instructions\" in {config}:  \"{include}\"",
        ];
    }

    private static string CannotImport(IncludeFacts facts, string harness, string file) =>
        $"{Indent}{harness}: {Show(facts, file)} cannot import a file and {harness} has no setting that adds one, "
        + $"so fleet's orchestrator instructions are not available for {harness}";

    private static bool Imports(IncludeFacts facts, string text) =>
        Import().Matches(Prose(text)).Any(m => Points(facts, m.Groups[1].Value));

    private static string Prose(string text)
    {
        var prose = new List<string>();
        var fenced = false;

        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)
                || line.TrimStart().StartsWith("~~~", StringComparison.Ordinal))
            {
                fenced = !fenced;
                continue;
            }

            if (!fenced)
            {
                prose.Add(CodeSpan().Replace(line, " "));
            }
        }

        return string.Join('\n', prose);
    }

    private static (bool Listed, string Problem) Listed(IncludeFacts facts, InstructionFile file)
    {
        if (file.Text is not { } json)
        {
            return (false, string.Empty);
        }

        try
        {
            using var document = JsonDocument.Parse(json, Lenient);

            var listed = document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("instructions", out var entries)
                && entries.ValueKind == JsonValueKind.Array
                && entries.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.String && Points(facts, e.GetString()!));

            return (listed, string.Empty);
        }
        catch (JsonException e)
        {
            return (false, $" ({Show(facts, file.Path)} could not be read: {e.Message})");
        }
    }

    private static bool Points(IncludeFacts facts, string target)
    {
        var resolved = target.StartsWith("~/", StringComparison.Ordinal) || target.StartsWith("~\\", StringComparison.Ordinal)
            ? Path.Combine(facts.Home, target[2..])
            : target;

        return Path.IsPathFullyQualified(resolved) && PathKey.Same(resolved, facts.IncludePath);
    }

    private static string Show(IncludeFacts facts, string path)
    {
        var shown = PathKey.Within(path, facts.Home)
            ? "~/" + Path.GetRelativePath(facts.Home, path)
            : path;

        return OperatingSystem.IsWindows() ? shown.Replace('\\', '/') : shown;
    }

    [GeneratedRegex(@"(?:^|\s)@(\S+)")]
    private static partial Regex Import();

    [GeneratedRegex("`[^`]*`")]
    private static partial Regex CodeSpan();
}
