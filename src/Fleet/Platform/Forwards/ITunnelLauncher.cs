namespace Fleet.Platform.Forwards;

public interface ITunnelLauncher
{
    ITunnel Start(IReadOnlyList<string> arguments);
}
