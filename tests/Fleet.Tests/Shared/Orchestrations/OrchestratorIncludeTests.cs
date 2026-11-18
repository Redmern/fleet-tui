using System.Text.RegularExpressions;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings;

namespace Fleet.Tests.Shared.Orchestrations;

public partial class OrchestratorIncludeTests
{
    [Fact]
    public void The_file_says_fleet_owns_and_overwrites_it()
    {
        var first = OrchestratorInclude.Text.Split('\n').Take(6);

        Assert.Contains(first, line => line.Contains("fleet owns this file", StringComparison.Ordinal)
            && line.Contains("overwrites", StringComparison.Ordinal));
    }

    [Fact]
    public void Before_any_section_the_text_tells_a_repository_agent_to_ignore_it()
    {
        var text = OrchestratorInclude.Text;
        var scope = text.IndexOf("A repository agent working on one branch ignores this file", StringComparison.Ordinal);

        Assert.InRange(scope, 0, text.IndexOf("\n## ", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("## Agent tools")]
    [InlineData("## Delegate first")]
    [InlineData("## Waiting for agents")]
    [InlineData("## Finishing work")]
    [InlineData("## Pushing and pull requests")]
    [InlineData("## Approvals")]
    public void The_text_has_each_section(string heading) =>
        Assert.Contains($"\n{heading}\n", OrchestratorInclude.Text);

    [Theory]
    [InlineData("list_agents")]
    [InlineData("agent_status")]
    [InlineData("new_agent")]
    [InlineData("dispatch")]
    [InlineData("tell_agent")]
    [InlineData("list_repositories")]
    [InlineData("report")]
    [InlineData("set_profile")]
    [InlineData("remove_agent")]
    [InlineData("open_pr")]
    public void The_text_names_the_fleet_tools_an_orchestrator_uses(string tool) =>
        Assert.Contains($"`{tool}", OrchestratorInclude.Text);

    [Fact]
    public void Every_tool_the_text_names_is_a_fleet_tool_or_open_pr()
    {
        var known = HarnessToolIds.All.Select(HarnessToolIds.For).Append("open_pr").ToHashSet();

        var named = ToolName().Matches(OrchestratorInclude.Text).Select(m => m.Groups[1].Value)
            .Where(n => n.Contains('_') || known.Contains(n))
            .Distinct()
            .ToList();

        Assert.NotEmpty(named);
        Assert.All(named, n => Assert.Contains(n, known));
    }

    [Fact]
    public void Pull_requests_go_through_open_pr_and_are_never_merged_or_opened_with_a_forge_cli()
    {
        var pushing = Section("## Pushing and pull requests");

        Assert.Contains("`open_pr`", pushing);
        Assert.Contains("never merge", pushing, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("run a forge command line to open one yourself", pushing, StringComparison.Ordinal);
        Assert.DoesNotContain("gh pr", OrchestratorInclude.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Workers_are_told_to_open_pull_requests_with_open_pr() =>
        Assert.Contains("`open_pr`", Section("## Delegate first"));

    private static string Section(string heading)
    {
        var text = OrchestratorInclude.Text;
        var start = text.IndexOf($"\n{heading}\n", StringComparison.Ordinal);
        var end = text.IndexOf("\n## ", start + heading.Length, StringComparison.Ordinal);

        Assert.True(start >= 0, heading);

        return end < 0 ? text[start..] : text[start..end];
    }

    [Fact]
    public void The_waiting_section_says_never_to_poll()
    {
        var text = OrchestratorInclude.Text;

        Assert.Contains("end your turn", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("never poll", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_text_says_only_the_user_approves_a_gate() =>
        Assert.Contains(ProcessText.OnlyTheUserApproves, OrchestratorInclude.Text);

    [Fact]
    public void The_text_is_agent_neutral_and_carries_no_personal_paths()
    {
        var text = OrchestratorInclude.Text;

        Assert.DoesNotContain("/home/", text);
        Assert.DoesNotContain("CLAUDE.md", text);
        Assert.DoesNotContain("SendMessage", text);
        Assert.DoesNotContain("\r", text);
        Assert.EndsWith("\n", text);
    }

    [Fact]
    public void The_file_lives_in_the_fleet_config_home()
    {
        var home = Path.Combine(Path.GetTempPath(), "fleet\\cfg");

        Assert.Equal(Path.Combine(home, "ORCHESTRATOR.md"), OrchestratorInclude.PathIn(home));
    }

    [GeneratedRegex(@"`([a-z]+(?:_[a-z]+)*)[`(]")]
    private static partial Regex ToolName();
}
