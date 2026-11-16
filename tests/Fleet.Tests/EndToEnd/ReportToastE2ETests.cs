using Fleet.Shared.Mcp;
using Fleet.Shared.Settings.Enums;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.EndToEnd;

public sealed class ReportToastE2ETests : ConfigHomeFixture
{
    private readonly MixedTeamRig _team;

    public ReportToastE2ETests() => _team = new MixedTeamRig(Path.Combine(ConfigHome, "pro\\ject"));

    [Theory]
    [InlineData("done")]
    [InlineData("failed")]
    public async Task A_report_queues_no_desktop_line_because_the_dashboards_done_and_failed_notice_toasts_it(string status)
    {
        var repository = await _team.AddRepositoryAsync("api");
        await _team.StartClaudeWorkerAsync(string.Empty, "api", repository, "fix");

        var reported = await _team.CallAsync(
            McpCaller.ForAgent("api", "fix"), HarnessTool.Report, [("status", status), ("summary", "x")]);

        Assert.False(reported.IsError, reported.Text);
        Assert.Empty(_team.DesktopNotifications);
    }
}
