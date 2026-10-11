using System.Text;
using System.Text.Json;

namespace Fleet.Platform.Jira;

public static class AdfText
{
    private static readonly HashSet<string> Blocks =
    [
        "paragraph", "heading", "bulletList", "orderedList", "listItem", "codeBlock", "blockquote", "rule",
        "panel", "table", "tableRow", "tableCell", "tableHeader", "mediaSingle", "mediaGroup", "expand", "nestedExpand",
    ];

    public static string From(JsonElement description)
    {
        if (description.ValueKind == JsonValueKind.String)
        {
            return description.GetString()!.Trim();
        }

        if (description.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        Block(description, text);

        return Tidy(text.ToString());
    }

    private static void Block(JsonElement node, StringBuilder text)
    {
        switch (Type(node))
        {
            case "paragraph":
                Paragraph(text, Inline(node));
                break;

            case "heading":
                var level = Math.Clamp(Number(node, "level") ?? 1, 1, 6);
                Paragraph(text, new string('#', level) + " " + Inline(node));
                break;

            case "codeBlock":
                Paragraph(text, "```\n" + Inline(node) + "\n```");
                break;

            case "rule":
                Paragraph(text, "---");
                break;

            case "bulletList":
                List(node, text, _ => "- ");
                break;

            case "orderedList":
                var start = Number(node, "order") ?? 1;
                List(node, text, i => $"{start + i}. ");
                break;

            case "blockquote":
                Paragraph(text, string.Join('\n', Lines(Children(node)).Select(l => l.Length == 0 ? ">" : "> " + l)));
                break;

            default:
                if (Content(node).Any(c => Blocks.Contains(Type(c))))
                {
                    foreach (var child in Content(node))
                    {
                        Block(child, text);
                    }
                }
                else
                {
                    Paragraph(text, Inline(node));
                }

                break;
        }
    }

    private static void List(JsonElement node, StringBuilder text, Func<int, string> marker)
    {
        var items = Content(node).ToList();
        var lines = new List<string>();

        for (var i = 0; i < items.Count; i++)
        {
            var mark = marker(i);
            var pad = new string(' ', mark.Length);
            var body = Lines(Children(items[i])).Where(l => l.Length > 0).DefaultIfEmpty(string.Empty).ToList();

            for (var j = 0; j < body.Count; j++)
            {
                lines.Add((j == 0 ? mark : pad) + body[j]);
            }
        }

        Paragraph(text, string.Join('\n', lines));
    }

    private static string Children(JsonElement node)
    {
        var inner = new StringBuilder();

        foreach (var child in Content(node))
        {
            Block(child, inner);
        }

        return Tidy(inner.ToString());
    }

    private static List<string> Lines(string text) =>
        text.Length == 0 ? [string.Empty] : [.. text.Split('\n')];

    private static string Inline(JsonElement node)
    {
        var text = new StringBuilder();
        InlineInto(node, text);
        return text.ToString();
    }

    private static void InlineInto(JsonElement node, StringBuilder text)
    {
        switch (Type(node))
        {
            case "text":
                text.Append(String(node, "text"));
                return;

            case "hardBreak":
                text.Append('\n');
                return;
        }

        var label = Attribute(node, "text") ?? Attribute(node, "shortName");

        if (label is not null)
        {
            text.Append(label);
            return;
        }

        foreach (var child in Content(node))
        {
            InlineInto(child, text);
        }
    }

    private static void Paragraph(StringBuilder text, string paragraph)
    {
        if (paragraph.Trim().Length > 0)
        {
            text.Append(paragraph.TrimEnd()).Append("\n\n");
        }
    }

    private static string Tidy(string text)
    {
        var lines = text.Replace("\r", string.Empty, StringComparison.Ordinal).Trim('\n').Split('\n');
        var kept = new List<string>();

        foreach (var line in lines)
        {
            if (line.Trim().Length == 0 && kept.Count > 0 && kept[^1].Length == 0)
            {
                continue;
            }

            kept.Add(line.Trim().Length == 0 ? string.Empty : line);
        }

        return string.Join('\n', kept);
    }

    private static string Type(JsonElement node) => String(node, "type");

    private static IEnumerable<JsonElement> Content(JsonElement node) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty("content", out var content)
        && content.ValueKind == JsonValueKind.Array
            ? content.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.Object)
            : [];

    private static string String(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : string.Empty;

    private static string? Attribute(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty("attrs", out var attrs)
        && attrs.ValueKind == JsonValueKind.Object
        && attrs.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Number(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty("attrs", out var attrs)
        && attrs.ValueKind == JsonValueKind.Object
        && attrs.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var number)
            ? number
            : null;
}
