namespace Fleet.Shared.Hooks;

public sealed record HookDecision(bool Take, string Task);

public static class HookPrompt
{
    public static HookDecision Intercepted(string prompt, string trigger)
    {
        if (trigger.Length == 0)
        {
            return new HookDecision(false, string.Empty);
        }

        var text = prompt.TrimStart();

        if (!text.StartsWith(trigger, StringComparison.Ordinal))
        {
            return new HookDecision(false, string.Empty);
        }

        var task = text[trigger.Length..].TrimStart();

        return task.Length == 0
            ? new HookDecision(false, string.Empty)
            : new HookDecision(true, task);
    }
}
