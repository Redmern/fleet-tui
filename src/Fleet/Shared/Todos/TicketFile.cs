using System.Globalization;
using System.Text;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Shared.Todos;

public static class TicketFile
{
    private const string Fence = "---";

    private const string CreatedFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    public static string Format(Ticket ticket)
    {
        var text = new StringBuilder();

        text.Append(Fence).Append('\n');
        Field(text, "id", ticket.Id);
        Field(text, "title", Quoted(ticket.Title));
        Field(text, "effort", TicketWords.Of(ticket.Effort));
        Field(text, "status", TicketWords.Of(ticket.Status));
        Field(text, "source", TicketWords.Of(ticket.Source));
        Field(text, "created", ticket.Created.ToUniversalTime().ToString(CreatedFormat, CultureInfo.InvariantCulture));
        Optional(text, "jira", ticket.Jira);
        Optional(text, "jira-closed", ticket.JiraClosed);
        Optional(text, "orchestration", ticket.Orchestration);
        Optional(text, "note", Quoted(ticket.Note));
        text.Append(Fence).Append('\n').Append('\n');
        text.Append(ticket.Body);

        return text.ToString();
    }

    public static bool IsValid(string text) => Read(text) is not null;

    public static Ticket Parse(string id, string text, DateTimeOffset fallbackCreated = default)
    {
        if (Read(text) is not { } read)
        {
            return new Ticket(id, id, null, TicketStatus.Open, TicketSource.Local, fallbackCreated, text);
        }

        var fields = read.Fields;

        return new Ticket(
            id,
            fields["title"],
            TicketWords.Effort(Value(fields, "effort")),
            TicketWords.Status(Value(fields, "status")),
            TicketWords.Source(Value(fields, "source")),
            ParseCreated(Value(fields, "created")) ?? fallbackCreated,
            read.Body)
        {
            Jira = Value(fields, "jira"),
            JiraClosed = Value(fields, "jira-closed"),
            Orchestration = Value(fields, "orchestration"),
            Note = Value(fields, "note"),
        };
    }

    private sealed record Frontmatter(Dictionary<string, string> Fields, string Body);

    private static Frontmatter? Read(string text)
    {
        var position = 0;

        if (NextLine(text, ref position) != Fence)
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);

        while (position < text.Length)
        {
            var line = NextLine(text, ref position);

            if (line == Fence)
            {
                if (!fields.TryGetValue("title", out var title) || title.Length == 0)
                {
                    return null;
                }

                var afterFence = position;

                if (NextLine(text, ref afterFence).Length == 0 && afterFence > position)
                {
                    position = afterFence;
                }

                return new Frontmatter(fields, text[position..]);
            }

            if (line.Trim().Length == 0 || line.TrimStart().StartsWith('#') || char.IsWhiteSpace(line[0]) || line[0] == '-')
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var key = colon > 0 ? line[..colon].Trim() : string.Empty;

            if (key.Length == 0 || !key.All(IsKeyChar))
            {
                return null;
            }

            fields[key] = Unquoted(line[(colon + 1)..].Trim());
        }

        return null;
    }

    private static bool IsKeyChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or '-';

    private static string NextLine(string text, ref int position)
    {
        var end = text.IndexOf('\n', position);
        var stop = end < 0 ? text.Length : end;
        var line = text[position..stop].TrimEnd('\r');

        position = end < 0 ? text.Length : end + 1;

        return line;
    }

    private static string Value(Dictionary<string, string> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : string.Empty;

    private static DateTimeOffset? ParseCreated(string text) =>
        DateTimeOffset.TryParse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var created)
            ? created
            : null;

    private static void Field(StringBuilder text, string key, string value)
    {
        text.Append(key).Append(':');

        if (value.Length > 0)
        {
            text.Append(' ').Append(value);
        }

        text.Append('\n');
    }

    private static void Optional(StringBuilder text, string key, string value)
    {
        if (value.Length > 0 && value != "\"\"")
        {
            Field(text, key, value);
        }
    }

    private static string Quoted(string value)
    {
        var line = string.Join(' ', value.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

        return "\"" + line.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static string Unquoted(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
        {
            return value[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
        {
            return value;
        }

        var text = new StringBuilder();

        for (var i = 1; i < value.Length - 1; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length - 1)
            {
                i++;
            }

            text.Append(value[i]);
        }

        return text.ToString();
    }
}
