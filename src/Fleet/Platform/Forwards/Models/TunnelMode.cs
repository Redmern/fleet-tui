using Fleet.Ports.Forwards;

namespace Fleet.Platform.Forwards.Models;

public sealed record TunnelMode(ITunnelLauncher Launcher, IListenerProbe Probe, TimeSpan UpWithin)
{
    public TimeSpan RestartAfter { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan RestartAtMost { get; init; } = TimeSpan.FromMinutes(1);

    public TimeProvider Time { get; init; } = TimeProvider.System;
}
