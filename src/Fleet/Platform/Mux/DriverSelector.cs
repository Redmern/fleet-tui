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

        if (env.EmbeddedReady)
        {
            return DriverNames.Embedded;
        }

        return env.Installed.Contains(DriverNames.Tmux) ? DriverNames.Tmux : DriverNames.Embedded;
    }
}
