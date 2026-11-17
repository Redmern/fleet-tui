using System.Text;

namespace Fleet.Features.Repositories.OpenPr;

public static class AgitPush
{
    public const string Base64Marker = "{base64}";

    public static IReadOnlyList<string> Args(string remote, string branch, string @base, string title, string body) =>
    [
        "push",
        remote,
        $"refs/heads/{branch}:refs/for/{@base}",
        "-o",
        $"topic={branch}",
        "-o",
        $"title={OneLine(title)}",
        "-o",
        $"description={Description(body)}",
    ];

    public static string OneLine(string text) =>
        string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    public static string Description(string body)
    {
        var text = body.ReplaceLineEndings("\n").Trim('\n');

        return text.Contains('\n', StringComparison.Ordinal) || text.StartsWith('{')
            ? Base64Marker + Convert.ToBase64String(Encoding.UTF8.GetBytes(text))
            : text;
    }
}
