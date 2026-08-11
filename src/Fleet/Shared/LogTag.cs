namespace Fleet.Shared;

public static class LogTag
{
    public static string For(string project, string message) => $"[{project}] {message}";

    public static (string Project, string Message) Split(string line)
    {
        if (!line.StartsWith('['))
        {
            return (string.Empty, line);
        }

        var close = line.IndexOf(']', StringComparison.Ordinal);

        return close > 1
            ? (line[1..close], line[(close + 1)..].TrimStart())
            : (string.Empty, line);
    }
}
