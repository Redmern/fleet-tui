using System.Text;
using System.Text.Json;
using Fleet.Platform.Mcp.Models;
using Fleet.Ports.Mcp.Models;

namespace Fleet.Platform.Mcp;

public static class JsonRpcCodec
{
    public static bool TryParse(string line, out RpcCall call)
    {
        call = new RpcCall(string.Empty, RpcId.Absent, string.Empty, new Dictionary<string, string>());

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("method", out var method)
                || method.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var id = RpcId.From(root);
            var name = string.Empty;
            var arguments = new Dictionary<string, string>();

            if (root.TryGetProperty("params", out var parameters)
                && parameters.ValueKind == JsonValueKind.Object)
            {
                if (parameters.TryGetProperty("name", out var toolName)
                    && toolName.ValueKind == JsonValueKind.String)
                {
                    name = toolName.GetString() ?? string.Empty;
                }

                if (parameters.TryGetProperty("arguments", out var args)
                    && args.ValueKind == JsonValueKind.Object)
                {
                    foreach (var field in args.EnumerateObject())
                    {
                        arguments[field.Name] = Flatten(field.Value);
                    }
                }
            }

            call = new RpcCall(method.GetString() ?? string.Empty, id, name, arguments);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public static string Result(RpcId id, string payload) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id.Raw},\"result\":{payload}}}";

    public static string Error(RpcId id, int code, string message)
    {
        var body = JsonSerializer.Serialize(
            new RpcErrorBody { Code = code, Message = message }, McpJsonContext.Default.RpcErrorBody);

        return $"{{\"jsonrpc\":\"2.0\",\"id\":{id.Raw},\"error\":{body}}}";
    }

    public static string Initialize(string protocolVersion, string serverName, string version) =>
        JsonSerializer.Serialize(
            new InitializeResult
            {
                ProtocolVersion = protocolVersion,
                ServerInfo = new ServerInfo { Name = serverName, Version = version },
            },
            McpJsonContext.Default.InitializeResult);

    public static string ToolList(IReadOnlyList<McpToolInfo> tools)
    {
        var builder = new StringBuilder("{\"tools\":[");

        for (var i = 0; i < tools.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder
                .Append("{\"name\":").Append(Str(tools[i].Name))
                .Append(",\"description\":").Append(Str(tools[i].Description))
                .Append(",\"inputSchema\":").Append(tools[i].InputSchemaJson)
                .Append('}');
        }

        return builder.Append("]}").ToString();
    }

    public static string Call(McpResult result) =>
        JsonSerializer.Serialize(
            new CallResult
            {
                Content = [new TextBlock { Text = result.Text }],
                IsError = result.IsError,
            },
            McpJsonContext.Default.CallResult);

    private static string Str(string value) =>
        JsonSerializer.Serialize(value, McpJsonContext.Default.String);

    private static string Flatten(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Number => value.GetRawText(),
        _ => value.GetRawText(),
    };
}
