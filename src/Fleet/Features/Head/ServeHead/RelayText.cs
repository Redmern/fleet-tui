namespace Fleet.Features.Head.ServeHead;

public static class RelayText
{
    public const string IntoNvimTerminal = "\u001c\u000ei";

    public const string Submit = "\r";

    public static string Dispatch(string prompt, string trigger)
    {
        var text = prompt.Trim().ReplaceLineEndings("\n");

        return trigger.Length == 0 || text.StartsWith(trigger, StringComparison.Ordinal)
            ? text
            : trigger + text;
    }
}
