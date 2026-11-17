using Fleet.Features.Agents.NewAgent.Models;

namespace Fleet.Features.Agents.NewAgent;

public static class BaseNote
{
    public static string For(StartedAgent started, string requestedBase)
    {
        var branch = started.Agent.Branch;
        var wanted = requestedBase.Trim();
        var tracked = started.Tracks.Length > 0;

        if (wanted.Length > 0 && started.ExistingTip.Length > 0)
        {
            return tracked
                ? $" base '{wanted}' was ignored: {branch} already exists on origin at {started.ExistingTip}; "
                  + $"the local branch tracks {started.Tracks}."
                : $" base '{wanted}' was ignored: {branch} already exists at {started.ExistingTip}.";
        }

        return tracked
            ? $" {branch} was only on origin: the local branch tracks {started.Tracks} at {started.ExistingTip}."
            : string.Empty;
    }
}
