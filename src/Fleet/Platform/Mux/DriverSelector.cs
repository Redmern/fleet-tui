using Fleet.Platform.Mux.Constants;
using Fleet.Platform.Mux.Models;

namespace Fleet.Platform.Mux;

public static class DriverSelector
{
    public static string Choose(MuxEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(env.Override))
        {
            return env.Override.Trim().ToLowerInvariant();
        }

        if (env.InsideTmux)
        {
            return DriverNames.Tmux;
        }

        if (env.InsideWezTerm)
        {
            return DriverNames.WezTerm;
        }

        if (env.GuiReachable && env.Installed.Contains(DriverNames.WezTerm))
        {
            return DriverNames.WezTerm;
        }

        return env.Installed.Contains(DriverNames.Tmux) ? DriverNames.Tmux : DriverNames.Embedded;
    }
}
