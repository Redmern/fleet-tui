using System.Text;

namespace Fleet.Shared;

public static class ProjectName
{
    /// <summary>
    /// Strips anything with no business in a filename, leaving letters, digits,
    /// underscore and dash. Returns an empty string when nothing usable is left,
    /// which callers must treat as an error rather than writing a file called
    /// ".json".
    /// </summary>
    /// <remarks>
    /// An allow-list rather than a deny-list. A project name becomes a path
    /// segment, so anything that could escape the config directory — separators,
    /// dots, drive letters — must not survive.
    /// </remarks>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
