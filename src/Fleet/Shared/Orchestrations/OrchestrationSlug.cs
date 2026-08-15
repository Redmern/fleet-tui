using System.Text;

namespace Fleet.Shared.Orchestrations;

public static class OrchestrationSlug
{
    private const int MaxWords = 6;

    private const int MaxLength = 40;

    public static string Of(string prompt)
    {
        var builder = new StringBuilder();
        var lastDash = false;
        var words = 0;

        foreach (var c in prompt.Trim().ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
                lastDash = false;
            }
            else if (!lastDash && builder.Length > 0)
            {
                if (++words >= MaxWords || builder.Length >= MaxLength)
                {
                    break;
                }

                builder.Append('-');
                lastDash = true;
            }
        }

        var slug = builder.ToString().Trim('-');

        if (slug.Length > MaxLength)
        {
            slug = slug[..MaxLength].Trim('-');
        }

        return slug.Length == 0 ? "task" : slug;
    }

    public static string Unique(string slug, Func<string, bool> taken)
    {
        if (!taken(slug))
        {
            return slug;
        }

        for (var n = 2; n <= 99; n++)
        {
            var candidate = $"{slug}-{n}";

            if (!taken(candidate))
            {
                return candidate;
            }
        }

        return $"{slug}-{Guid.NewGuid():N}"[..Math.Min(MaxLength, slug.Length + 9)];
    }
}
