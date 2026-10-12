namespace Fleet.Features.Head.ServeHead;

public static class RelayText
{
    public const string IntoNvimTerminal = "\u001c\u000ei";

    public const string Submit = "\r";

    public static string Keys(string text, bool inNvim) => inNvim ? IntoNvimTerminal + text : text;

    public const string AfterTrigger = " ";

    public static string Dispatch(string prompt, string trigger)
    {
        var task = Plain(prompt, trigger);

        return trigger.Length == 0 || task.Length == 0
            ? task
            : trigger + AfterTrigger + task;
    }

    public static string Late(string text, DateTimeOffset queuedAt) =>
        text + "\n\n(fleet: the head queued this at "
        + queuedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture)
        + " and it is delivered late; the head may have sent a newer prompt since, so check before doing it twice.)";

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
