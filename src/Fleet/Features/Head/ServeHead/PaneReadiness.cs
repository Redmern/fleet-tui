using Fleet.Features.Head.ServeHead.Enums;

namespace Fleet.Features.Head.ServeHead;

public static class PaneReadiness
{
    private static readonly string[] InputMarkers =
        ["for shortcuts", "shift+tab to cycle", "❯", "> "];

    private static readonly string[] WaitingMarkers =
        ["do you want", "waiting for your input", "no, and tell claude"];

    public static Readiness Classify(string paneText)
    {
        var text = paneText.ToLowerInvariant();

        if (text.Trim().Length == 0)
        {
            return Readiness.Starting;
        }

        if (text.Contains("esc to interrupt", StringComparison.Ordinal))
        {
            return Readiness.Busy;
        }

        if (WaitingMarkers.Any(m => text.Contains(m, StringComparison.Ordinal)))
        {
            return Readiness.Waiting;
        }

        return InputMarkers.Any(m => text.Contains(m, StringComparison.Ordinal))
            ? Readiness.Ready
            : Readiness.Starting;
    }

    public static Readiness Settled(string first, string second) =>
        first == second ? Classify(second) : Readiness.Busy;
}
