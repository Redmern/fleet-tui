namespace Fleet.Features.Head.ServeHead;

public static class RelayText
{
    public const string IntoNvimTerminal = "\u001c\u000ei";

    public const string Submit = "\r";

    public static string Keys(string text, bool inNvim) => inNvim ? IntoNvimTerminal + text : text;

    public static string Dispatch(string prompt, string trigger)
    {
        var text = prompt.Trim().ReplaceLineEndings("\n");

        return trigger.Length == 0 || text.StartsWith(trigger, StringComparison.Ordinal)
            ? text
            : trigger + text;
    }

    public static string Plain(string prompt, string trigger)
    {
        var text = prompt.Trim().ReplaceLineEndings("\n");

        while (trigger.Length > 0 && text.StartsWith(trigger, StringComparison.Ordinal))
        {
            text = text[trigger.Length..].TrimStart();
        }

        return text;
    }
}
