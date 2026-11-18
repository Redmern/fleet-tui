namespace Fleet.Features.Repositories.OpenPr;

public sealed record RemoteAddress(string Host, string Path, bool Web, string Authority)
{
    public string ProbeTarget => Web ? Authority : Host;

    public static string Redacted(string url)
    {
        var text = url.Trim();
        var scheme = text.IndexOf("://", StringComparison.Ordinal);
        var at = text.IndexOf('@', StringComparison.Ordinal);
        var slash = scheme < 0 ? -1 : text.IndexOf('/', scheme + 3);

        return scheme >= 0 && at > scheme && (slash < 0 || at < slash)
            ? text[..(scheme + 3)] + text[(at + 1)..]
            : text;
    }

    public const string GitHubHost = "github.com";

    public bool IsGitHub => string.Equals(Host, GitHubHost, StringComparison.OrdinalIgnoreCase);

    public string Slug => Path.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? Path[..^4] : Path;

    public string GitHubRepository => IsGitHub ? Slug : $"{Authority}/{Slug}";

    public string? WebBase => Web && Slug.Length > 0 ? $"https://{Authority}/{Slug}" : null;

    public static RemoteAddress? Parse(string url)
    {
        var text = url.Trim();

        if (text.Length == 0)
        {
            return null;
        }

        if (text.Contains("://", StringComparison.Ordinal))
        {
            return Uri.TryCreate(text, UriKind.Absolute, out var parsed) && parsed.Host.Length > 0 && !parsed.IsFile
                ? new RemoteAddress(
                    parsed.Host.ToLowerInvariant(),
                    parsed.AbsolutePath.Trim('/'),
                    parsed.Scheme is "https" or "http",
                    parsed.Authority.ToLowerInvariant())
                : null;
        }

        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var slash = text.IndexOfAny(['/', '\\']);

        if (colon <= 1 || (slash >= 0 && slash < colon))
        {
            return null;
        }

        var host = text[..colon];
        var at = host.LastIndexOf('@');

        host = at >= 0 ? host[(at + 1)..] : host;

        return host.Length > 0 && Uri.CheckHostName(host) != UriHostNameType.Unknown
            ? new RemoteAddress(host.ToLowerInvariant(), text[(colon + 1)..].Trim('/'), false, host.ToLowerInvariant())
            : null;
    }
}
