using Fleet.Features.Setup.RunSetup;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Harness;

namespace Fleet.Tests.Features.Setup;

public sealed class HarnessCheckTests
{
    private static SetupHandler Handler(params string[] installed) => new(t => installed.Contains(t));

    [Fact]
    public void A_harness_in_use_that_is_missing_is_reported_with_its_install_command()
    {
        var report = Handler("git", "nvim", "claude", "yazi").Inspect("/cfg", inUse: ["opencode"]);

        var step = Assert.Single(report.Missing);
        Assert.Equal("opencode", step.Name);
        Assert.False(step.Required);
        Assert.False(report.Blocked);
        Assert.Contains("npm install -g opencode-ai", step.Fix, StringComparison.Ordinal);
        Assert.Contains("cannot start", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_registered_harness_not_in_use_is_offered_but_never_counts_as_missing()
    {
        var report = Handler("git", "nvim", "claude", "yazi").Inspect("/cfg");

        var step = Assert.Single(report.Steps, s => s.Name == "opencode");
        Assert.True(step.Ok);
        Assert.Contains("not in use", step.Detail, StringComparison.Ordinal);
        Assert.Contains("npm install -g opencode-ai", step.Detail, StringComparison.Ordinal);
        Assert.Empty(report.Missing);
    }

    [Fact]
    public void A_harness_in_use_that_is_installed_is_found()
    {
        var report = Handler("git", "nvim", "claude", "yazi", "opencode").Inspect("/cfg", inUse: ["opencode"]);

        var step = Assert.Single(report.Steps, s => s.Name == "opencode");
        Assert.True(step.Ok);
        Assert.Contains("found on PATH", step.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void Claude_is_checked_once_as_before()
    {
        var report = Handler("git").Inspect("/cfg", inUse: ["claude"]);

        Assert.Single(report.Steps, s => s.Name == "claude");
    }

    [Fact]
    public void Every_registered_harness_has_an_install_hint()
    {
        Assert.All(HarnessRegistry.Profiles, p => Assert.NotEqual(string.Empty, SetupHints.For(p.Binary)));
        Assert.All(HarnessRegistry.Profiles, p => Assert.DoesNotContain("put it on PATH", SetupHints.For(p.Binary), StringComparison.Ordinal));
    }

    [Fact]
    public void Claude_has_no_degraded_capabilities()
    {
        Assert.Empty(HarnessCheck.Degraded(HarnessRegistry.Claude));
    }

    [Fact]
    public void Opencode_states_what_it_cannot_do()
    {
        var degraded = HarnessCheck.Degraded(HarnessRegistry.Opencode);

        Assert.Contains(degraded, d => d.Contains("no status hooks", StringComparison.Ordinal));
        Assert.Contains(degraded, d => d.Contains("no permission policy", StringComparison.Ordinal));
        Assert.Contains(degraded, d => d.Contains("typed into its pane", StringComparison.Ordinal));
        Assert.DoesNotContain(degraded, d => d.Contains("skills", StringComparison.Ordinal));
    }

    [Fact]
    public void Doctor_lines_report_each_registered_harness_with_path_use_and_degradation()
    {
        var lines = HarnessCheck.DoctorLines(t => t == "claude", new Dictionary<string, int> { ["claude"] = 3, ["opencode"] = 1 });

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("claude", lines[0], StringComparison.Ordinal);
        Assert.Contains("on PATH", lines[0], StringComparison.Ordinal);
        Assert.Contains("3 agent(s)", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("opencode", lines[1], StringComparison.Ordinal);
        Assert.Contains("NOT FOUND", lines[1], StringComparison.Ordinal);
        Assert.Contains("1 agent(s)", lines[1], StringComparison.Ordinal);
        Assert.Contains("no status hooks", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Doctor_lines_say_when_a_harness_is_not_in_use()
    {
        var lines = HarnessCheck.DoctorLines(_ => false, new Dictionary<string, int>());

        Assert.Contains("not in use", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Harnesses_in_use_count_agents_and_orchestrators_but_not_editor_only_panes()
    {
        static AgentRecord A(string harness) => new("/w", "r", "b", harness, "main", false);

        var inUse = HarnessCheck.InUse([A("claude"), A("orchestrator"), A("opencode"), A("nvim")]);

        Assert.Equal(2, inUse["claude"]);
        Assert.Equal(1, inUse["opencode"]);
        Assert.False(inUse.ContainsKey("nvim"));
    }

    [Fact]
    public void A_sub_orchestrator_counts_under_the_harness_it_runs()
    {
        var sub = new AgentRecord("/o", "", "add-login", "orchestrator", "", false, OrchestratorHarness: "opencode");

        Assert.Equal(1, HarnessCheck.InUse([sub])["opencode"]);
    }
}
