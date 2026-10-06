namespace Fleet.Shared.Messaging;

public static class PeerMessage
{
    public const string CrossSessionTag = "<cross-session-message";

    public const string TaskNotificationTag = "<task-notification>";

    public static bool FromClaude(string prompt)
    {
        var text = prompt.TrimStart();

        return text.StartsWith(CrossSessionTag, StringComparison.Ordinal)
               || text.StartsWith(TaskNotificationTag, StringComparison.Ordinal);
    }

    public static string SendYourself(string target, string address, string message, string? unreachable = null) =>
        $"Not typed into its pane: {target} takes cross-session messages, so deliver it yourself now. "
        + $"Call SendMessage with to \"{address}\" and this message, unchanged:\n\n{message}\n\n"
        + "If a delivery notice says it was held or refused, that is the receiving session's choice: "
        + "tell your user instead of retrying."
        + (unreachable is null ? string.Empty : $" {unreachable}");

    public static string DispatchRequest(string task) =>
        "The head orchestrator asks you to dispatch a sub-orchestrator for the task below. "
        + "Call your fleet dispatch tool with it as the message; don't do the task yourself.\n\n"
        + task;
}
