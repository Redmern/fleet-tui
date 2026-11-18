namespace Fleet.Features.Repositories.OpenPr;

public static class GitNames
{
    public static string? Refusal(string what, string name)
    {
        var text = name.Trim();

        if (text.Length == 0)
        {
            return $"The {what} is empty.";
        }

        return text.StartsWith('-') || text.StartsWith('+') || text.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is ':' or '^' or '~' or '?' or '*' or '[' or '\\')
            ? $"'{text}' is not a usable {what}."
            : null;
    }
}
