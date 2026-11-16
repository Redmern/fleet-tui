using Fleet.Features.Mcp.ServeMcp;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Mcp;

public class FloatingApprovalChannelTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

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
        await mux.ShowWorkspaceAsync("techweb");

        var asking = Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

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
        await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        await mux.ShowWorkspaceAsync("techweb");
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.Null(_inner.Asked!.Pane);
        Assert.DoesNotContain("spawn-float", mux.Calls);
    }

    [Fact]
    public async Task A_multiplexer_without_floats_leaves_the_request_to_the_dashboard()
    {
        var mux = new FakeMuxDriver();
        mux.CurrentPane = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.Null(_inner.Asked!.Pane);
        Assert.DoesNotContain("spawn-float", mux.Calls);
    }

    [Fact]
    public async Task A_request_for_a_project_no_client_shows_fails_at_once_without_waiting()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        var agent = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        await mux.SpawnAsync(new SpawnOptions { SessionName = "fleet", Args = ["claude"] });
        mux.CurrentPane = agent;
        await mux.ShowWorkspaceAsync("fleet");

        var outcome = await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.False(outcome.Allowed);
        Assert.Equal("approval needed in project techweb; open it to answer", outcome.Reason);
        Assert.Null(_inner.Asked);
        Assert.DoesNotContain("spawn-float", mux.Calls);
    }

    [Fact]
    public async Task A_project_another_client_shows_still_gets_the_prompt()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        var agent = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        mux.CurrentPane = agent;
        mux.CurrentClient = "desktop";
        await mux.ShowWorkspaceAsync("techweb");
        mux.CurrentClient = "mcp-server";
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        var outcome = await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.True(outcome.Allowed);
        Assert.NotNull(_inner.Asked);
    }

    [Fact]
    public async Task A_multiplexer_without_workspaces_keeps_waiting_for_an_answer()
    {
        var mux = new FakeMuxDriver();
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        var outcome = await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.True(outcome.Allowed);
        Assert.NotNull(_inner.Asked);
    }

    [Fact]
    public async Task A_daemon_that_does_not_say_who_shows_the_project_keeps_the_prompt_waiting()
    {
        var mux = new FakeMuxDriver(workspaces: true, reportsWhoShows: false);
        var agent = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        mux.CurrentPane = agent;
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        var outcome = await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.True(outcome.Allowed);
        Assert.NotNull(_inner.Asked);
    }

    [Fact]
    public async Task A_project_with_no_workspace_at_all_fails_at_once_because_nobody_can_show_it()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        await mux.SpawnAsync(new SpawnOptions { SessionName = "fleet", Args = ["claude"] });

        var outcome = await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.Equal("approval needed in project techweb; open it to answer", outcome.Reason);
        Assert.Null(_inner.Asked);
    }

    [Fact]
    public async Task A_hidden_sub_nobody_shows_fails_at_once()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        mux.CurrentPane = await mux.SpawnAsync(
            new SpawnOptions { SessionName = FleetWorkspaces.HiddenFor("techweb"), Args = ["claude"] });
        await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });

        var outcome = await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.Equal("approval needed in project techweb; open it to answer", outcome.Reason);
        Assert.Null(_inner.Asked);
    }

    [Fact]
    public async Task A_hidden_sub_still_gets_its_prompt_in_the_dashboard_of_a_project_someone_shows()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        mux.CurrentPane = await mux.SpawnAsync(
            new SpawnOptions { SessionName = FleetWorkspaces.HiddenFor("techweb"), Args = ["claude"] });
        await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        await mux.ShowWorkspaceAsync("techweb");
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        var outcome = await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.True(outcome.Allowed);
        Assert.NotNull(_inner.Asked);
    }

    [Fact]
    public async Task A_caller_whose_own_workspace_is_shown_keeps_the_prompt_even_when_the_project_is_not()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        mux.CurrentPane = await mux.SpawnAsync(new SpawnOptions { SessionName = "side", Args = ["claude"] });
        await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        await mux.ShowWorkspaceAsync("side");
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        var outcome = await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.True(outcome.Allowed);
        Assert.NotNull(_inner.Asked);
    }

    [Fact]
    public async Task A_mux_that_cannot_answer_the_question_keeps_the_prompt_waiting()
    {
        var mux = new FakeMuxDriver(workspaces: true);
        mux.CurrentPane = await mux.SpawnAsync(new SpawnOptions { SessionName = "techweb", Args = ["claude"] });
        mux.Available = false;
        _inner.Reply.SetResult(ApprovalOutcome.Allow);

        var outcome = await Channel(mux).AskAsync(Ask()).WaitAsync(Patience);

        Assert.True(outcome.Allowed);
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
