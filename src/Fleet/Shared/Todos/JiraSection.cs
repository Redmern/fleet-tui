namespace Fleet.Shared.Todos;

public static class JiraSection
{
    public const string Start = "<!-- jira:start -->";

    public const string End = "<!-- jira:end -->";

    public static string Replace(string body, string description)
    {
        var text = description
            .Replace("<!-- jira:", "<!-- jira\u200b:", StringComparison.Ordinal)
            .Trim('\r', '\n', ' ', '\t');
        var section = text.Length == 0 ? $"{Start}\n{End}" : $"{Start}\n{text}\n{End}";

        var start = body.IndexOf(Start, StringComparison.Ordinal);
        var end = start < 0 ? -1 : body.IndexOf(End, start, StringComparison.Ordinal);

        if (end >= 0)
        {
            return body[..start] + section + body[(end + End.Length)..];
        }

        if (start >= 0)
        {
            return body[..start] + section + "\n";
        }

        return body.Trim().Length == 0 ? section + "\n" : section + "\n\n" + body;
    }

    public static bool IsKey(string key)
    {
        var dash = key.LastIndexOf('-');

        return dash > 0
            && dash < key.Length - 1
            && char.IsAsciiLetterUpper(key[0])
            && key[..dash].All(c => char.IsAsciiLetterUpper(c) || char.IsAsciiDigit(c) || c == '_')
            && key[(dash + 1)..].All(char.IsAsciiDigit);
    }
}
