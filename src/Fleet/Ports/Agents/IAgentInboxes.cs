namespace Fleet.Ports.Agents;

public interface IAgentInboxes
{
    Task<string?> AddressAsync(string folder, CancellationToken ct = default);
}
