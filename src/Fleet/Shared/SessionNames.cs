using System.Text;

namespace Fleet.Shared;

public static class SessionNames
{
    public const string Head = "fleet-head";

    public const string MainSuffix = "main";

    public const string SubMarker = "sub";

    public static string MainOrchestrator(string project) => Join(project, MainSuffix);

    public static string SubOrchestrator(string project, string slug) => Join(project, SubMarker, slug);

    public static string RepoAgent(string project, string repository, string branch) =>
        Join(project, repository, branch);

    public static string ForAgent(string project, string repository, string branch, bool orchestrator) =>
        orchestrator
            ? SubOrchestrator(project, branch)
            : RepoAgent(project, repository, branch);

    public static string Part(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '_')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string Join(params string[] parts) =>
        string.Join('-', parts.Select(Part).Where(p => p.Length > 0));
}
