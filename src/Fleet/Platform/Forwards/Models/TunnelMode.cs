using Fleet.Ports.Forwards;

namespace Fleet.Platform.Forwards.Models;

public sealed record TunnelMode(ITunnelLauncher Launcher, IListenerProbe Probe, TimeSpan UpWithin);
