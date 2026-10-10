using Fleet.Shared.Constants;
using Fleet.Shared.Iso.Models;

namespace Fleet.Shared.Iso;

public sealed class IsoCodes
{
    public const string ProjectPrefix = "sub";

    public const string AgentPrefix = "agent";

    public const string Unknown = "unknown";

    private const int LongestCode = 40;

    private static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;

    private readonly Dictionary<string, string> _codeOf = new(Names);

    private readonly Dictionary<string, string> _nameOf = new(Names);

    private readonly Dictionary<string, List<string>> _worktrees = new(Names);

    private IsoCodes()
    {
    }

    public static bool IsValidCode(string code) =>
        code.Length is > 0 and <= LongestCode && code.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public static IsoCodes Assign(
        IsoConfig config, IEnumerable<string> projects, Func<string, IEnumerable<string>> worktrees)
    {
        var codes = new IsoCodes();
        var names = projects
            .Select(p => p.Trim())
            .Where(p => p.Length > 0 && !IsFleetWorkspace(p) && !FleetWorkspaces.IsHidden(p))
            .Distinct(Names)
            .Order(Names)
            .ToList();

        foreach (var name in names)
        {
            if (config.Codes.TryGetValue(name, out var chosen) && IsValidCode(chosen) && !codes._nameOf.ContainsKey(chosen))
            {
                codes.Add(name, chosen);
            }
        }

        var next = 1;

        foreach (var name in names.Where(n => !codes._codeOf.ContainsKey(n)))
        {
            string code;

            do
            {
                code = $"{ProjectPrefix}{next++}";
            }
            while (codes._nameOf.ContainsKey(code));

            codes.Add(name, code);
        }

        foreach (var name in names)
        {
            codes._worktrees[name] = [.. worktrees(name)
                .Where(w => w.Length > 0)
                .Select(PathKey.For)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];
        }

        return codes;
    }

    public string Project(string name) => _codeOf.GetValueOrDefault(name.Trim(), Unknown);

    public string? ProjectNamed(string code) => _nameOf.GetValueOrDefault(code.Trim());

    public string Workspace(string name) =>
        IsFleetWorkspace(name) || string.Equals(name, FleetWorkspaces.Hidden, StringComparison.OrdinalIgnoreCase)
            ? name
            : name.EndsWith(FleetWorkspaces.HiddenSuffix, StringComparison.OrdinalIgnoreCase)
                ? FleetWorkspaces.HiddenFor(Project(name[..^FleetWorkspaces.HiddenSuffix.Length]))
                : Project(name);

    public string? WorkspaceNamed(string code) =>
        IsFleetWorkspace(code) || string.Equals(code, FleetWorkspaces.Hidden, StringComparison.OrdinalIgnoreCase)
            ? code
            : code.EndsWith(FleetWorkspaces.HiddenSuffix, StringComparison.OrdinalIgnoreCase)
                ? ProjectNamed(code[..^FleetWorkspaces.HiddenSuffix.Length]) is { } hidden
                    ? FleetWorkspaces.HiddenFor(hidden)
                    : null
                : ProjectNamed(code);

    public string Agent(string project, string worktree)
    {
        var index = _worktrees.TryGetValue(project.Trim(), out var known)
            ? known.IndexOf(PathKey.For(worktree))
            : -1;

        return index < 0 ? $"{Project(project)}.{AgentPrefix}" : $"{Project(project)}.{AgentPrefix}{index + 1}";
    }

    public string? WorktreeOf(string project, string agentCode)
    {
        var prefix = $"{Project(project)}.{AgentPrefix}";

        return _worktrees.TryGetValue(project.Trim(), out var known)
               && agentCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && int.TryParse(agentCode[prefix.Length..], out var number)
               && number >= 1 && number <= known.Count
            ? known[number - 1]
            : null;
    }

    private void Add(string name, string code)
    {
        _codeOf[name] = code;
        _nameOf[code] = name;
    }

    private static bool IsFleetWorkspace(string name) =>
        string.Equals(name, FleetWorkspaces.Default, StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, FleetWorkspaces.Head, StringComparison.OrdinalIgnoreCase);
}
