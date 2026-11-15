using Fleet.Platform.Mcp;

namespace Fleet.Platform.Mcp.Models;

public sealed record RpcCall(
    string Method,
    RpcId Id,
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments)
{
    public RpcId CancelledId { get; init; } = RpcId.Absent;

    public string? ProgressToken { get; init; }

    public bool WantsReply => Id.Present;
}
