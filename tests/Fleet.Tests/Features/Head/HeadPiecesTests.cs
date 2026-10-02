using Fleet.Features.Head.ServeHead;
using Fleet.Features.Head.ServeHead.Enums;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Features.Head;

public sealed class HeadPiecesTests
{
    [Theory]
    [InlineData("", Readiness.Starting)]
    [InlineData("NVIM v0.11", Readiness.Starting)]
    [InlineData("* Churning... (3s · esc to interrupt)\n> ", Readiness.Busy)]
    [InlineData("Do you want to proceed?\n❯ 1. Yes", Readiness.Waiting)]
    [InlineData("│ > \n  ? for shortcuts", Readiness.Ready)]
    [InlineData("\u276F \n  ⏵⏵ auto mode on (shift+tab to cycle)", Readiness.Ready)]
    public void A_pane_reads_as_ready_only_at_an_empty_claude_prompt(string text, Readiness expected)
    {
        Assert.Equal(expected, PaneReadiness.Classify(text));
    }

    [Fact]
    public void A_screen_that_is_still_changing_is_busy()
    {
        Assert.Equal(Readiness.Busy, PaneReadiness.Settled("? for shortcuts", "? for shortcuts x"));
        Assert.Equal(Readiness.Ready, PaneReadiness.Settled("? for shortcuts", "? for shortcuts"));
    }

    [Theory]
    [InlineData("do it", ",", ",do it")]
    [InlineData(",do it", ",", ",do it")]
    [InlineData("  two\r\nlines ", ",", ",two\nlines")]
    [InlineData("plain", "", "plain")]
    public void The_dispatch_text_carries_the_trigger_once(string prompt, string trigger, string expected)
    {
        Assert.Equal(expected, RelayText.Dispatch(prompt, trigger));
    }

    [Fact]
    public void Relayed_keys_leave_any_nvim_mode_and_enter_the_terminal_first()
    {
        Assert.Equal("\u001c\u000ei", RelayText.IntoNvimTerminal);
    }

    [Fact]
    public void The_main_pane_is_the_root_pane_that_is_not_the_dashboard_or_hidden()
    {
        IReadOnlyList<Pane> panes =
        [
            Pane("p1", "C:/p/web", "web~hidden", "dash"),
            Pane("p2", "C:/p/web", "default", "dash"),
            Pane("p3", "C:/p/web", "default", "dash"),
            Pane("p4", "C:/p/web", "fleet-head", "head"),
            Pane("p5", "C:/p/api", "default", "dash"),
        ];

        Assert.Equal("p3", MainPane.Find(panes, "C:/p/web", "p2")?.Id.Value);
        Assert.Equal("p2", MainPane.Dashboard(panes, "p2")?.Id.Value);
        Assert.Null(MainPane.Find(panes, "C:/p/none", null));
    }

    [Fact]
    public void Every_head_tool_has_a_unique_name_and_a_description()
    {
        Assert.Equal(HeadTools.Names.Count, HeadTools.Names.Distinct().Count());
        Assert.All(HeadTools.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
        Assert.Contains(HeadTools.Relay, HeadTools.Names);
    }

    [Fact]
    public void The_head_starts_claude_with_its_voice_settings_and_resumes_after_the_first_run()
    {
        Assert.Equal(["--settings", "on.json"], HeadLaunch.ClaudeArgs("on.json", resume: false));
        Assert.Equal(["--settings", "off.json", "--continue"], HeadLaunch.ClaudeArgs("off.json", resume: true));
        Assert.Contains("\"enabled\":true", HeadLaunch.VoiceOn);
        Assert.Contains("\"enabled\":false", HeadLaunch.VoiceOff);
        Assert.Equal(["mcp", "--head"], HeadLaunch.McpArgs);
    }

    [Fact]
    public void The_brief_names_every_tool()
    {
        Assert.All(HeadTools.Names, n => Assert.Contains($"`{n}`", HeadBrief.Text));
    }

    private static Pane Pane(string id, string cwd, string session, string title) =>
        new(new PaneId(id), "w1", "t1", session, title, cwd, false);
}
