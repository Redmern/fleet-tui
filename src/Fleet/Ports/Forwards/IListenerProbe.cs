namespace Fleet.Ports.Forwards;

public interface IListenerProbe
{
    Task<bool> ListeningAsync(int port, CancellationToken ct = default);
}
