using System.Text;

namespace Fleet.Features.Mcp.ServeMcp;

public static class SafeText
{
    public const int MaxValue = 120;

    public static string Clean(string value, int max = MaxValue)
    {
        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            var next = char.IsControl(ch) || ch == ' ' ? ' ' : ch;

            if (next == ' ' && builder.Length > 0 && builder[^1] == ' ')
            {
                continue;
            }

            builder.Append(next);
        }

        var trimmed = builder.ToString().Trim();

        return trimmed.Length <= max ? trimmed : trimmed[..(max - 1)] + "…";
    }
}
