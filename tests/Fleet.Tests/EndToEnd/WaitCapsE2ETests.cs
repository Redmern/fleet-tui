using Fleet.Shared.Harness;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings.Enums;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.EndToEnd;

// AC-9: wait_for_agent waits 45 s by default and clamps a longer wait to the cap of the calling harness, on the
// fake clock. McpActions is the real one, as in MixedTeamE2ETests.
public sealed class WaitCapsE2ETests : ConfigHomeFixture
{
    private const string Main = "";

    private readonly MixedTeamRig _team;

    public WaitCapsE2ETests() => _team = new MixedTeamRig(Path.Combine(ConfigHome, "pro\\ject"));

    private static (string, string)[] Wait(string branch, string timeout) =>
        [("repository", "api"), ("branch", branch), ("timeout", timeout)];

    private async Task BusyClaudeWorkerAsync()
    {
        var repository = await _team.AddRepositoryAsync("api");
        await _team.StartClaudeWorkerAsync(Main, "api", repository, "busy");
    }

    private async Task<string> StartedAsync(string harness)
    {
        var started = await _team.CallAsync(
            Main, HarnessTool.NewAgent,
            ("repository", "api"), ("branch", harness), ("harness", harness), ("task", "wait for busy"));

        Assert.False(started.IsError, started.Text);

        return McpCaller.ForAgent("api", harness);
    }

    [Fact]
    public async Task A_codex_caller_asking_for_300_seconds_is_clamped_to_55_on_the_fake_clock()
    {
        await BusyClaudeWorkerAsync();
        var codex = await StartedAsync(HarnessRegistry.CodexName);
        var before = _team.Clock.GetUtcNow();

        var waited = await _team.CallAsync(codex, HarnessTool.WaitForAgent, Wait("busy", "300"));

        Assert.False(waited.IsError, waited.Text);
        Assert.StartsWith("api/busy: still working after 55s", waited.Text);
        Assert.Contains("clamped to 55s for codex", waited.Text);
        Assert.InRange(_team.Clock.GetUtcNow() - before, TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task A_codex_caller_that_gives_no_timeout_waits_45_seconds_unclamped()
    {
        await BusyClaudeWorkerAsync();
        var codex = await StartedAsync(HarnessRegistry.CodexName);

        var waited = await _team.CallAsync(codex, HarnessTool.WaitForAgent, [("repository", "api"), ("branch", "busy")]);

        Assert.StartsWith("api/busy: still working after 45s", waited.Text);
        Assert.DoesNotContain("clamped", waited.Text);
    }

    [Fact]
    public async Task A_claude_caller_keeps_a_wait_that_a_codex_caller_would_have_clamped()
    {
        await BusyClaudeWorkerAsync();

        var waited = await _team.CallAsync(Main, HarnessTool.WaitForAgent, Wait("busy", "100"));

        Assert.StartsWith("api/busy: still working after 100s", waited.Text);
        Assert.DoesNotContain("clamped", waited.Text);
    }

    [Fact]
    public async Task A_caller_fleet_has_no_record_of_is_clamped_to_the_strictest_cap()
    {
        await BusyClaudeWorkerAsync();

        var waited = await _team.CallAsync(McpCaller.ForAgent("api", "gone"), HarnessTool.WaitForAgent, Wait("busy", "300"));

        Assert.StartsWith("api/busy: still working after 55s", waited.Text);
        Assert.Contains("clamped to 55s for an unknown harness", waited.Text);
    }
}
