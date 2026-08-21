namespace Fleet.Features.Agents.ListAgents;

public static class AgentActivity
{
    public const string Working = "working";

    public const string Waiting = "waiting";

    public const string Idle = "idle";

    public static string Classify(string paneText)
    {
        if (paneText.Trim().Length == 0)
        {
            return string.Empty;
        }

        var text = paneText.ToLowerInvariant();

        if (text.Contains("esc to interrupt"))
        {
            return Working;
        }

        if (text.Contains("do you want")
            || text.Contains("waiting for your input")
            || text.Contains("no, and tell claude"))
        {
            return Waiting;
        }

        return Idle;
    }
}
