using Fleet.Features.Mcp.ServeMcp;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Features.Mcp;

public class FloatingApprovalChannelTests
{
    private readonly Inner _inner = new();

    private FloatingApprovalChannel Channel(FakeMuxDriver mux) =>
        new(_inner, mux, (project, pane) => ["fleet", "approve", "--project", project, pane]);

    private static ApprovalRequest Ask() => new("techweb", "new_agent", "upgrade wants to start backend/login");

    [Fact]
    public async Task A_request_from_an_agent_pane_opens_an_approval_float_over_it_until_it_is_answered()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        var agent = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        mux.CurrentPane = agent;

        var asking = Channel(mux).AskAsync(Ask());

        Assert.Equal(agent.Value, _inner.Asked!.Pane);
        var popup = (await mux.ListPanesAsync()).Single(p => p.TabId == "float");
        Assert.Equal("techweb", popup.SessionName);
        Assert.Equal(["fleet", "approve", "--project", "techweb", agent.Value], mux.ArgsFor(popup.Id));

        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        Assert.True((await asking).Allowed);
        Assert.DoesNotContain(await mux.ListPanesAsync(), p => p.Id == popup.Id);
    }

    [Fact]
    public async Task Outside_a_pane_the_request_goes_to_the_dashboard_as_before()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        await Channel(mux).AskAsync(Ask());

        Assert.Null(_inner.Asked!.Pane);
        Assert.DoesNotContain("spawn-float", mux.Calls);
    }

    [Fact]
    public async Task A_multiplexer_without_floats_leaves_the_request_to_the_dashboard()
    {
        var mux = new FakeMuxDriver();
        mux.CurrentPane = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        await Channel(mux).AskAsync(Ask());

        Assert.Null(_inner.Asked!.Pane);
        Assert.DoesNotContain("spawn-float", mux.Calls);
    }

    private sealed class Inner : IApprovalChannel
    {
        public ApprovalRequest? Asked { get; private set; }

        public TaskCompletionSource<ApprovalOutcome> Reply { get; } = new();

        public Task<ApprovalOutcome> AskAsync(ApprovalRequest request, CancellationToken ct = default)
        {
            Asked = request;
            return Reply.Task;
        }
    }
}
