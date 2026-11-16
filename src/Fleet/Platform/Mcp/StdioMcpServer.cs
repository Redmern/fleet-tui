using Fleet.Ports;
using Fleet.Ports.Mcp;
using Fleet.Ports.Mcp.Models;
using Fleet.Platform.Mcp.Models;

namespace Fleet.Platform.Mcp;

public sealed class StdioMcpServer(
    TextReader input, TextWriter output, IFleetLog log, bool oneCallAtATime = false) : IMcpServer
{
    public const string ProtocolVersion = "2024-11-05";

    public const string Version = "0.1.0";

    private readonly SemaphoreSlim _writing = new(1, 1);

    public async Task RunAsync(McpServing serving, CancellationToken ct = default)
    {
        var running = new HashSet<Task>();
        var served = oneCallAtATime ? serving with { Invoke = InTurn(serving.Invoke) } : serving;

        try
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

                if (call.Method == "tools/call")
                {
                    running.RemoveWhere(task => task.IsCompleted);
                    running.Add(Task.Run(() => RunCall(call, served, ct), CancellationToken.None));

                    continue;
                }

                if (ReplyFor(call, serving) is { } reply)
                {
                    await Send(reply, ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await Task.WhenAll(running).ConfigureAwait(false);
        }
    }

    private static string? ReplyFor(RpcCall call, McpServing serving)
    {
        switch (call.Method)
        {
            case "initialize":
                serving.OnReady?.Invoke();
                return JsonRpcCodec.Result(
                    call.Id,
                    JsonRpcCodec.Initialize(ProtocolVersion, serving.ServerName, Version));

            case "tools/list":
                return JsonRpcCodec.Result(call.Id, JsonRpcCodec.ToolList(serving.Tools));

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

    private async Task RunCall(RpcCall call, McpServing serving, CancellationToken ct)
    {
        var reply = await CallReply(call, serving, ct).ConfigureAwait(false);

        await Send(reply, ct).ConfigureAwait(false);
    }

    private async Task<string> CallReply(RpcCall call, McpServing serving, CancellationToken ct)
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
        await _writing.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            await output.WriteAsync(message.AsMemory(), ct).ConfigureAwait(false);
            await output.WriteAsync('\n').ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _writing.Release();
        }
    }

    private static Func<McpRequest, CancellationToken, Task<McpResult>> InTurn(
        Func<McpRequest, CancellationToken, Task<McpResult>> invoke)
    {
        var turn = new SemaphoreSlim(1, 1);

        return async (request, ct) =>
        {
            await turn.WaitAsync(ct).ConfigureAwait(false);

            try
            {
                return await invoke(request, ct).ConfigureAwait(false);
            }
            finally
            {
                turn.Release();
            }
        };
    }
}
