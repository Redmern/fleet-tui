using Fleet.Ports.Mcp.Models;

namespace Fleet.Ports.Mcp;

public interface IMcpServer
{
    Task RunAsync(McpServing serving, CancellationToken ct = default);
}
