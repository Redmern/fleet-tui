namespace Fleet.Ports.Mcp.Models;

public sealed record McpServing(
    string ServerName,
    IReadOnlyList<McpToolInfo> Tools,
    Func<McpRequest, CancellationToken, Task<McpResult>> Invoke);
