namespace Fleet.Features.Repositories.PullRepository;

public static class PullOutcome
{
    public const string AlreadyCurrent = "already up to date";

    public static string Summarise(string output)
    {
        var text = output.Trim();

        if (text.Length == 0)
        {
            return AlreadyCurrent;
        }

        if (text.Contains("Already up to date", StringComparison.OrdinalIgnoreCase))
        {
            return AlreadyCurrent;
        }

        var first = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();

        return first.Length > 90 ? first[..90] : first;
    }
}
