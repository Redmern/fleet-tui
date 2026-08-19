using Fleet.Features.Mcp.ServeMcp;
using Fleet.Ports;
using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Settings;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Mcp;

public sealed class McpDispatcherTests
{
    private readonly FakeSettings _settings = new();

    private readonly FakeApprovals _approvals = new();

    private readonly FakeLog _log = new();

    private int _performed;

    private static McpRequest Request(string tool) => new(tool, new Dictionary<string, string>());

    private McpDispatcher Dispatcher(string caller = "") => new(
        McpCaller.AtRoot("techweb", "C:/repos/techweb") with { Caller = caller },
        _settings,
        _approvals,
        _log,
        (_, _) =>
        {
            _performed++;
            return Task.FromResult(McpResult.Ok("done"));
        });

    [Fact]
    public async Task An_unknown_tool_never_reaches_the_handler()
    {
        var result = await Dispatcher().HandleAsync(Request("teleport"));

        Assert.True(result.IsError);
        Assert.Equal(0, _performed);
    }

    [Fact]
    public async Task A_read_tool_runs_without_asking()
    {
        var result = await Dispatcher().HandleAsync(Request("list_agents"));

        Assert.False(result.IsError);
        Assert.Equal(1, _performed);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task A_forbidden_tool_is_refused_before_the_handler()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.RemoveRepository, ActionPolicy.Forbid);

        var result = await Dispatcher().HandleAsync(Request("remove_repository"));

        Assert.True(result.IsError);
        Assert.Equal(0, _performed);
        Assert.Contains(_log.Lines, l => l.Contains("not allowed"));
    }

    [Fact]
    public async Task An_ask_tool_prompts_the_dashboard_and_runs_when_allowed()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        _approvals.Answer = ApprovalOutcome.Allow;

        var result = await Dispatcher().HandleAsync(Request("new_agent"));

        Assert.False(result.IsError);
        Assert.Equal(1, _performed);
        Assert.Single(_approvals.Asked);
    }

    [Fact]
    public async Task A_denied_ask_returns_the_reason_and_skips_the_handler()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);
        _approvals.Answer = ApprovalOutcome.Deny("you said no");

        var result = await Dispatcher().HandleAsync(Request("new_agent"));

        Assert.True(result.IsError);
        Assert.Equal("you said no", result.Text);
        Assert.Equal(0, _performed);
    }

    [Fact]
    public async Task A_claude_only_ask_passes_through_because_claude_already_prompted()
    {
        _settings.Config = SettingsConfig.Default
            .With(HarnessTool.NewAgent, ActionPolicy.Ask)
            .With(HarnessTool.NewAgent, AskChannel.ClaudePermission);

        var result = await Dispatcher().HandleAsync(Request("new_agent"));

        Assert.False(result.IsError);
        Assert.Equal(1, _performed);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task A_handler_that_fails_is_audited_as_denied()
    {
        var dispatcher = new McpDispatcher(
            McpCaller.AtRoot("techweb", "C:/repos/techweb"),
            _settings,
            _approvals,
            _log,
            (_, _) => Task.FromResult(McpResult.Error("git blew up")));

        var result = await dispatcher.HandleAsync(Request("list_agents"));

        Assert.True(result.IsError);
        Assert.Contains(_log.Lines, l => l.Contains("git blew up"));
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public SettingsConfig Config { get; set; } = SettingsConfig.Default;

        public SettingsConfig Load(string project) => Config;

        public void Save(string project, SettingsConfig config) => Config = config;
    }

    private sealed class FakeApprovals : IApprovalChannel
    {
        public ApprovalOutcome Answer { get; set; } = ApprovalOutcome.Allow;

        public List<ApprovalRequest> Asked { get; } = [];

        public Task<ApprovalOutcome> AskAsync(ApprovalRequest request, CancellationToken ct = default)
        {
            Asked.Add(request);
            return Task.FromResult(Answer);
        }
    }

    private sealed class FakeLog : IFleetLog
    {
        public List<string> Lines { get; } = [];

        public void Swallowed(Exception e) { }

        public void Write(string line) => Lines.Add(line);

        public IReadOnlyList<string> Tail(int lines) => Lines;
    }
}
