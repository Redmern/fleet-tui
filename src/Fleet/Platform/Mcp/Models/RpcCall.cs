using Fleet.Platform.Mcp;

namespace Fleet.Platform.Mcp.Models;

public sealed record RpcCall(
    string Method,
    RpcId Id,
    string ToolName,
    IReadOnlyDictionary<string, string> Arguments)
{
    public bool WantsReply => Id.Present;
}
