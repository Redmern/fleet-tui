using Fleet.Platform.Mcp;
using Fleet.Ports;
using Fleet.Ports.Mcp.Models;

namespace Fleet.Tests.Platform.Mcp;

public sealed class StdioMcpServerTests
{
    private readonly FakeLog _log = new();

    private static McpServing Serving(Func<McpRequest, McpResult> perform) => new(
        "fleet",
        [new McpToolInfo("list_agents", "List agents", """{"type":"object"}""")],
        (request, _) => Task.FromResult(perform(request)));

    private async Task<IReadOnlyList<string>> Run(string transcript, McpServing serving)
    {
        var output = new StringWriter();

        await new StdioMcpServer(new StringReader(transcript), output, _log).RunAsync(serving);

        return output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public async Task Initialize_announces_the_server_by_name()
    {
        var replies = await Run(
            """{"id":0,"method":"initialize"}""" + "\n",
            Serving(_ => McpResult.Ok("")));

        Assert.Single(replies);
        Assert.Contains("\"serverInfo\"", replies[0]);
        Assert.Contains("\"name\":\"fleet\"", replies[0]);
        Assert.Contains("\"id\":0", replies[0]);
    }

    [Fact]
    public async Task Tools_list_returns_the_served_tools()
    {
        var replies = await Run(
            """{"id":1,"method":"tools/list"}""" + "\n",
            Serving(_ => McpResult.Ok("")));

        Assert.Contains("list_agents", replies[0]);
    }

    [Fact]
    public async Task A_tool_call_reaches_the_handler_and_returns_its_text()
    {
        var replies = await Run(
            """{"id":2,"method":"tools/call","params":{"name":"list_agents","arguments":{}}}""" + "\n",
            Serving(request => McpResult.Ok($"invoked {request.Tool}")));

        Assert.Contains("invoked list_agents", replies[0]);
        Assert.Contains("\"isError\":false", replies[0]);
    }

    [Fact]
    public async Task A_notification_draws_no_reply()
    {
        var replies = await Run(
            """{"method":"notifications/initialized"}""" + "\n",
            Serving(_ => McpResult.Ok("")));

        Assert.Empty(replies);
    }

    [Fact]
    public async Task An_unknown_method_with_an_id_returns_a_method_not_found_error()
    {
        var replies = await Run(
            """{"id":9,"method":"dance"}""" + "\n",
            Serving(_ => McpResult.Ok("")));

        Assert.Contains("-32601", replies[0]);
        Assert.Contains("\"id\":9", replies[0]);
    }

    [Fact]
    public async Task A_handler_that_throws_is_reported_as_an_error_not_a_crash()
    {
        var replies = await Run(
            """{"id":3,"method":"tools/call","params":{"name":"list_agents","arguments":{}}}""" + "\n",
            Serving(_ => throw new InvalidOperationException("boom")));

        Assert.Contains("\"isError\":true", replies[0]);
        Assert.NotEmpty(_log.Swallows);
    }

    [Fact]
    public async Task A_whole_session_replies_in_order_and_skips_the_notification()
    {
        var transcript = string.Join('\n',
            """{"id":0,"method":"initialize"}""",
            """{"method":"notifications/initialized"}""",
            """{"id":1,"method":"tools/list"}""",
            """{"id":2,"method":"tools/call","params":{"name":"list_agents","arguments":{}}}""") + "\n";

        var replies = await Run(transcript, Serving(_ => McpResult.Ok("ok")));

        Assert.Equal(3, replies.Count);
        Assert.Contains("serverInfo", replies[0]);
        Assert.Contains("list_agents", replies[1]);
        Assert.Contains("\"id\":2", replies[2]);
    }

    private sealed class FakeLog : IFleetLog
    {
        public List<Exception> Swallows { get; } = [];

        public void Swallowed(Exception e) => Swallows.Add(e);

        public void Write(string line) { }

        public IReadOnlyList<string> Tail(int lines) => [];
    }
}
