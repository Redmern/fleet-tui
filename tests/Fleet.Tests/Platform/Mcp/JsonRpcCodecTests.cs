using Fleet.Platform.Mcp;
using Fleet.Ports.Mcp.Models;

namespace Fleet.Tests.Platform.Mcp;

public sealed class JsonRpcCodecTests
{
    [Fact]
    public void A_tools_call_yields_the_tool_name_and_flattened_arguments()
    {
        var line =
            """{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"new_agent","arguments":{"repository":"backend","branch":"login"}}}""";

        Assert.True(JsonRpcCodec.TryParse(line, out var call));
        Assert.Equal("tools/call", call.Method);
        Assert.Equal("new_agent", call.ToolName);
        Assert.Equal("backend", call.Arguments["repository"]);
        Assert.Equal("login", call.Arguments["branch"]);
    }

    [Fact]
    public void Non_string_argument_values_are_flattened_to_text()
    {
        var line =
            """{"id":1,"method":"tools/call","params":{"name":"remove_agent","arguments":{"delete_worktree":true,"count":3,"note":null}}}""";

        Assert.True(JsonRpcCodec.TryParse(line, out var call));
        Assert.Equal("true", call.Arguments["delete_worktree"]);
        Assert.Equal("3", call.Arguments["count"]);
        Assert.Equal(string.Empty, call.Arguments["note"]);
    }

    [Fact]
    public void Malformed_json_is_rejected_rather_than_thrown()
    {
        Assert.False(JsonRpcCodec.TryParse("{ this is not json", out _));
        Assert.False(JsonRpcCodec.TryParse("", out _));
    }

    [Fact]
    public void A_message_with_no_method_is_not_a_call()
    {
        Assert.False(JsonRpcCodec.TryParse("""{"id":1,"result":{}}""", out _));
    }

    [Theory]
    [InlineData("""{"id":7,"method":"ping"}""", "7")]
    [InlineData("""{"id":"abc","method":"ping"}""", "\"abc\"")]
    public void The_id_round_trips_byte_for_byte(string line, string expectedRaw)
    {
        Assert.True(JsonRpcCodec.TryParse(line, out var call));

        var envelope = JsonRpcCodec.Result(call.Id, "{}");

        Assert.Equal($"{{\"jsonrpc\":\"2.0\",\"id\":{expectedRaw},\"result\":{{}}}}", envelope);
    }

    [Fact]
    public void A_notification_has_no_id_to_reply_to()
    {
        Assert.True(JsonRpcCodec.TryParse("""{"method":"notifications/initialized"}""", out var call));

        Assert.False(call.WantsReply);
    }

    [Fact]
    public void The_tool_list_carries_each_tools_schema_inline()
    {
        var tools = new[]
        {
            new McpToolInfo("list_agents", "List agents", """{"type":"object"}"""),
        };

        var payload = JsonRpcCodec.ToolList(tools);

        Assert.Equal(
            """{"tools":[{"name":"list_agents","description":"List agents","inputSchema":{"type":"object"}}]}""",
            payload);
    }

    [Fact]
    public void A_call_result_reports_its_text_and_error_flag()
    {
        var payload = JsonRpcCodec.Call(McpResult.Error("nope"));

        Assert.Equal(
            """{"content":[{"type":"text","text":"nope"}],"isError":true}""",
            payload);
    }

    [Fact]
    public void An_error_envelope_carries_the_code_and_message()
    {
        Assert.True(JsonRpcCodec.TryParse("""{"id":2,"method":"boom"}""", out var call));

        var envelope = JsonRpcCodec.Error(call.Id, -32601, "Unknown method boom.");

        Assert.Equal(
            """{"jsonrpc":"2.0","id":2,"error":{"code":-32601,"message":"Unknown method boom."}}""",
            envelope);
    }
}
