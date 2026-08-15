using System.Text.Json;

namespace Fleet.Platform.Mcp;

public readonly struct RpcId
{
    private RpcId(string raw, bool present)
    {
        Raw = raw;
        Present = present;
    }

    public string Raw { get; }

    public bool Present { get; }

    public static RpcId Absent => new("null", false);

    public static RpcId From(JsonElement parent)
    {
        if (!parent.TryGetProperty("id", out var id) || id.ValueKind == JsonValueKind.Null)
        {
            return Absent;
        }

        return new RpcId(id.GetRawText(), true);
    }
}
