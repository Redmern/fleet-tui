using System.Globalization;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings.Enums;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.EndToEnd;

// AC-1, AC-2, AC-5: tell_agent stamps the instruction time on the record before it delivers, and a finished agent
// goes back to working. McpActions is the real one, as in MixedTeamE2ETests.
public sealed class InstructedAtE2ETests : ConfigHomeFixture
{
    private const string Main = "";

    private readonly MixedTeamRig _team;

    public InstructedAtE2ETests() => _team = new MixedTeamRig(Path.Combine(ConfigHome, "pro\\ject"));

    private static (string, string)[] To(string repository, string branch, string message) =>
        [("repository", repository), ("branch", branch), ("message", message)];

    private static (string, string)[] Agent(string repository, string branch) =>
        [("repository", repository), ("branch", branch)];

    private static (string, string)[] Status(string status, string summary) =>
        [("status", status), ("summary", summary)];

    private string Now() => _team.Clock.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);

    private async Task<AgentRecord> DoneWorkerAsync(string summary)
    {
        var repository = await _team.AddRepositoryAsync("api");
        await _team.StartClaudeWorkerAsync(Main, "api", repository, "fix");
        var reported = await _team.CallAsync(McpCaller.ForAgent("api", "fix"), HarnessTool.Report, Status("done", summary));

        Assert.False(reported.IsError, reported.Text);

        return _team.Record("api", "fix");
    }

    [Fact]
    public async Task A_typed_tell_to_a_done_agent_puts_it_back_to_working_and_stamps_the_instruction_time()
    {
        var before = await DoneWorkerAsync("first");

        Assert.Equal(OrchestrationStatus.Done, before.Status);
        Assert.Equal(string.Empty, before.InstructedAt);
        var at = Now();

        var told = await _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "now do the second part"));

        Assert.False(told.IsError, told.Text);
        var after = _team.Record("api", "fix");
        Assert.Equal(OrchestrationStatus.Working, after.Status);
        Assert.Equal(at, after.InstructedAt);
    }

    [Fact]
    public async Task A_tell_by_SendMessage_stamps_the_instruction_time_and_resets_a_failed_agent()
    {
        var repository = await _team.AddRepositoryAsync("api");
        var fix = await _team.StartClaudeWorkerAsync(Main, "api", repository, "fix");
        await _team.ClaudeHookReportsInboxAsync(fix, "uds:fix");
        await _team.CallAsync(McpCaller.ForAgent("api", "fix"), HarnessTool.Report, Status("failed", "broke"));
        var at = Now();

        var told = await _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "try again"));

        Assert.Contains("uds:fix", told.Text);
        var after = _team.Record("api", "fix");
        Assert.Equal(OrchestrationStatus.Working, after.Status);
        Assert.Equal(at, after.InstructedAt);
    }

    [Fact]
    public async Task A_tell_to_an_agent_that_is_not_open_changes_nothing()
    {
        var before = await DoneWorkerAsync("first");
        await _team.Mux.KillPaneAsync(_team.Mux.HarnessIn(before.Worktree).Pane);

        var told = await _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "more"));

        Assert.True(told.IsError);
        Assert.Equal(before, _team.Record("api", "fix"));
    }

    [Fact]
    public async Task A_tell_whose_delivery_fails_puts_the_record_back()
    {
        var before = await DoneWorkerAsync("first");
        _team.Mux.BeforeSend = _ => throw new IOException("pane gone");

        await Assert.ThrowsAsync<IOException>(
            () => _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "more")));

        Assert.Equal(before, _team.Record("api", "fix"));
    }

    [Fact]
    public async Task A_failed_delivery_does_not_undo_a_newer_instruction()
    {
        await DoneWorkerAsync("first");
        _team.Mux.BeforeSend = async _ =>
        {
            _team.Mux.BeforeSend = null;
            _team.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "newer"));

            throw new IOException("pane gone");
        };

        await Assert.ThrowsAsync<IOException>(
            () => _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "older")));

        var after = _team.Record("api", "fix");
        Assert.Equal(OrchestrationStatus.Working, after.Status);
        Assert.NotEqual(string.Empty, after.InstructedAt);
    }

    [Fact]
    public async Task A_done_report_that_lands_while_the_tell_is_being_typed_stays_current()
    {
        await DoneWorkerAsync("first");
        _team.Mux.BeforeSend = async _ =>
        {
            _team.Mux.BeforeSend = null;
            _team.Clock.Advance(TimeSpan.FromMilliseconds(1));
            await _team.CallAsync(McpCaller.ForAgent("api", "fix"), HarnessTool.Report, Status("done", "raced"));
        };

        var told = await _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "more"));

        Assert.False(told.IsError, told.Text);
        var after = _team.Record("api", "fix");
        Assert.Equal(OrchestrationStatus.Done, after.Status);
        Assert.True(DateTimeOffset.Parse(after.ReportedAt, CultureInfo.InvariantCulture) > DateTimeOffset.Parse(after.InstructedAt, CultureInfo.InvariantCulture));

        var waited = await _team.CallAsync(Main, HarnessTool.WaitForAgent, Agent("api", "fix"));

        Assert.Equal("api/fix: done — raced", waited.Text);
    }

    [Fact]
    public async Task A_tell_whose_stamp_cannot_be_stored_still_delivers_and_says_so_in_the_log()
    {
        await DoneWorkerAsync("first");
        _team.Store.RefuseUpdates = true;

        var told = await _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "more"));

        Assert.Equal("sent to api/fix.", told.Text);
        Assert.Contains(_team.Log.Tail(20), l => l.Contains("could not record the instruction time", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_done_report_after_a_tell_ends_the_wait()
    {
        await DoneWorkerAsync("first");
        await _team.CallAsync(Main, HarnessTool.TellAgent, To("api", "fix", "now do the second part"));
        await _team.CallAsync(McpCaller.ForAgent("api", "fix"), HarnessTool.Report, Status("done", "second"));

        var waited = await _team.CallAsync(Main, HarnessTool.WaitForAgent, Agent("api", "fix"));

        Assert.Equal("api/fix: done — second", waited.Text);
    }
}
