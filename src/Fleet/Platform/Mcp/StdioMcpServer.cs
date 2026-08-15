using Fleet.Ports;
using Fleet.Ports.Mcp;
using Fleet.Ports.Mcp.Models;
using Fleet.Platform.Mcp.Models;

namespace Fleet.Platform.Mcp;

public sealed class StdioMcpServer(TextReader input, TextWriter output, IFleetLog log) : IMcpServer
{
    public const string ProtocolVersion = "2024-11-05";

    public const string Version = "0.1.0";

    public async Task RunAsync(McpServing serving, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await input.ReadLineAsync(ct).ConfigureAwait(false);

            if (line is null)
            {
                return;
            }

            if (!JsonRpcCodec.TryParse(line, out var call))
            {
                continue;
            }

            var reply = await ReplyFor(call, serving, ct).ConfigureAwait(false);

            if (reply is not null)
            {
                await Send(reply, ct).ConfigureAwait(false);
            }
        }
    }

    private async Task<string?> ReplyFor(RpcCall call, McpServing serving, CancellationToken ct)
    {
        switch (call.Method)
        {
            case "initialize":
                return JsonRpcCodec.Result(
                    call.Id,
                    JsonRpcCodec.Initialize(ProtocolVersion, serving.ServerName, Version));

            case "tools/list":
                return JsonRpcCodec.Result(call.Id, JsonRpcCodec.ToolList(serving.Tools));

            case "tools/call":
                return await CallReply(call, serving, ct).ConfigureAwait(false);

            case "ping":
                return JsonRpcCodec.Result(call.Id, "{}");

            default:
                if (call.Method.StartsWith("notifications/", StringComparison.Ordinal)
                    || !call.WantsReply)
                {
                    return null;
                }

                return JsonRpcCodec.Error(call.Id, -32601, $"Unknown method '{call.Method}'.");
        }
    }

    private async Task<string?> CallReply(RpcCall call, McpServing serving, CancellationToken ct)
    {
        try
        {
            var result = await serving
                .Invoke(new McpRequest(call.ToolName, call.Arguments), ct)
                .ConfigureAwait(false);

            return JsonRpcCodec.Result(call.Id, JsonRpcCodec.Call(result));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            log.Swallowed(e);

            return JsonRpcCodec.Result(
                call.Id, JsonRpcCodec.Call(McpResult.Error("fleet hit an unexpected error.")));
        }
    }

    private async Task Send(string message, CancellationToken ct)
    {
        await output.WriteAsync(message.AsMemory(), ct).ConfigureAwait(false);
        await output.WriteAsync('\n').ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
    }
}
