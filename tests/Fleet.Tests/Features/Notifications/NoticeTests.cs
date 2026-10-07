using Fleet.Features.Notifications.DetectNotices;
using Fleet.Features.Notifications.SyncNotices;
using Fleet.Platform.Storage;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ports.Notifications.Models;
using Fleet.Shared.Hooks;
using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Tests.Features.Notifications;

[Collection(ConfigHomeCollection.Name)]
public sealed class NoticeTests : ConfigHomeFixture
{
    private static readonly DateTime T0 = new(2026, 9, 29, 12, 0, 0, DateTimeKind.Utc);

    private static readonly string Work = OperatingSystem.IsWindows() ? "C:/work" : "/work";

    private static AgentRecord Agent(string branch = "feat-x", string status = "", bool open = true) =>
        new($"{Work}/{branch}", "api", branch, "claude", "origin/main", false, Open: open, Status: status);

    private static AgentWatch Watch(
        AgentRecord agent, string text = "", bool alive = true, int unchangedMinutes = 0, int behind = 0, bool conflicts = false, bool lost = false) =>
        new(agent, alive, text, TimeSpan.FromMinutes(unchangedMinutes), behind, conflicts, lost);

    private static Notice Found(NoticeKind kind, string branch = "feat-x") =>
        new("alpha", kind, $"{Work}/{branch}", $"api / {branch}", "m", T0);

    [Fact]
    public void Each_reason_is_detected_from_what_the_dashboard_sees()
    {
        var found = NoticeDetector.Detect("alpha", [
            Watch(Agent("q"), "Which option should I take? (waiting for your input)"),
            Watch(Agent("p"), "Do you want to make this edit?\n1. Yes\n2. No, and tell Claude"),
            Watch(Agent("d", status: "done")),
            Watch(Agent("f", status: "failed")),
            Watch(Agent("gone"), alive: false, lost: true),
            Watch(Agent("slow"), "* Churning... (esc to interrupt)", unchangedMinutes: 12),
            Watch(Agent("behind"), behind: 25),
            Watch(Agent("clash"), conflicts: true),
            Watch(Agent("fine"), "* Churning... (esc to interrupt)", unchangedMinutes: 3),
        ], T0);

        Assert.Equal(
            [
                (NoticeKind.NeedsInput, "q"), (NoticeKind.Permission, "p"), (NoticeKind.Done, "d"), (NoticeKind.Failed, "f"),
                (NoticeKind.Failed, "gone"), (NoticeKind.Stalled, "slow"), (NoticeKind.BranchTrouble, "behind"), (NoticeKind.BranchTrouble, "clash"),
            ],
            found.Select(n => (n.Kind, n.Worktree.Split('/')[^1])));
        Assert.Contains("25 commits behind origin/main", found.Single(n => n.Worktree.EndsWith("behind", StringComparison.Ordinal)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sub_orchestrator_waiting_for_input_raises_a_notice_under_its_slug()
    {
        var sub = new AgentRecord(
            $"{Work}/.fleet/orchestrations/upgrade", string.Empty, "upgrade", "orchestrator", "origin/main", false, Open: true);

        var found = NoticeDetector.Detect("alpha", [
            Watch(sub, "Which option should I take? (waiting for your input)"),
            Watch(sub with { Worktree = $"{Work}/.fleet/orchestrations/ask", Branch = "ask" }) with { Hooked = Hook(AgentState.Blocked, HookStatus.InputReason) },
        ], T0);

        Assert.Equal(
            [(NoticeKind.NeedsInput, "sub / upgrade"), (NoticeKind.NeedsInput, "sub / ask")],
            found.Select(n => (n.Kind, n.Agent)));
    }

    private static AgentReport Hook(AgentState state, string reason = "", int minutesAgo = 0) =>
        new($"{Work}/x", "s1", state, T0 - TimeSpan.FromMinutes(minutesAgo), Reason: reason);

    [Fact]
    public void Hook_state_raises_permission_question_and_stall_notices()
    {
        var found = NoticeDetector.Detect("alpha", [
            Watch(Agent("p")) with { Hooked = Hook(AgentState.Blocked, HookStatus.PermissionReason) },
            Watch(Agent("q")) with { Hooked = Hook(AgentState.Blocked, HookStatus.InputReason) },
            Watch(Agent("slow"), "* Churning... (esc to interrupt)") with { Hooked = Hook(AgentState.Stalled, minutesAgo: 14) },
            Watch(Agent("busy")) with { Hooked = Hook(AgentState.Working) },
            Watch(Agent("rest")) with { Hooked = Hook(AgentState.Idle) },
        ], T0);

        Assert.Equal(
            [(NoticeKind.Permission, "p"), (NoticeKind.NeedsInput, "q"), (NoticeKind.Stalled, "slow")],
            found.Select(n => (n.Kind, n.Worktree.Split('/')[^1])));
        Assert.Contains("14 min", found.Single(n => n.Kind == NoticeKind.Stalled).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hook_stall_without_the_busy_spinner_is_an_interrupted_turn_not_a_stall()
    {
        var found = NoticeDetector.Detect("alpha", [
            Watch(Agent("esc"), "Interrupted by user\n> ") with { Hooked = Hook(AgentState.Stalled, minutesAgo: 30) },
            Watch(Agent("unread")) with { Hooked = Hook(AgentState.Stalled, minutesAgo: 30) },
        ], T0);

        Assert.Empty(found);
    }

    [Fact]
    public void A_hook_block_is_released_once_the_pane_runs_the_approved_tool()
    {
        var found = NoticeDetector.Detect("alpha", [
            Watch(Agent("approved"), "Bash(dotnet test)\n* Running... (esc to interrupt)") with { Hooked = Hook(AgentState.Blocked, HookStatus.PermissionReason) },
            Watch(Agent("asking"), "Do you want to proceed?\n2. No, and tell Claude") with { Hooked = Hook(AgentState.Blocked, HookStatus.PermissionReason) },
        ], T0);

        Assert.Equal([(NoticeKind.Permission, "asking")], found.Select(n => (n.Kind, n.Worktree.Split('/')[^1])));
    }

    [Fact]
    public void A_hook_stall_with_a_prompt_on_screen_raises_the_prompts_notice()
    {
        var found = NoticeDetector.Detect("alpha", [
            Watch(Agent("p"), "Do you want to proceed?\n2. No, and tell Claude") with { Hooked = Hook(AgentState.Stalled, minutesAgo: 15) },
            Watch(Agent("q"), "Which option should I take? (waiting for your input)") with { Hooked = Hook(AgentState.Stalled, minutesAgo: 15) },
        ], T0);

        Assert.Equal(
            [(NoticeKind.Permission, "p"), (NoticeKind.NeedsInput, "q")],
            found.Select(n => (n.Kind, n.Worktree.Split('/')[^1])));
    }

    [Fact]
    public void A_permission_prompt_dismissed_with_esc_resolves_but_a_question_stays()
    {
        var found = NoticeDetector.Detect("alpha", [
            Watch(Agent("esc"), "Interrupted by user\n> ") with { Hooked = Hook(AgentState.Blocked, HookStatus.PermissionReason) },
            Watch(Agent("unread")) with { Hooked = Hook(AgentState.Blocked, HookStatus.PermissionReason) },
            Watch(Agent("asks"), "Pick a server to connect\n> ") with { Hooked = Hook(AgentState.Blocked, HookStatus.InputReason) },
        ], T0);

        Assert.Equal(
            [(NoticeKind.Permission, "unread"), (NoticeKind.NeedsInput, "asks")],
            found.Select(n => (n.Kind, n.Worktree.Split('/')[^1])));
    }

    [Fact]
    public void Hook_state_wins_over_what_the_pane_shows()
    {
        var found = NoticeDetector.Detect("alpha", [
            Watch(Agent("p"), "Do you want to make this edit?\n2. No, and tell Claude") with { Hooked = Hook(AgentState.Working) },
        ], T0);

        Assert.Empty(found);
    }

    [Fact]
    public void Without_hook_state_the_pane_text_is_still_the_fallback()
    {
        var found = NoticeDetector.Detect("alpha", [
            Watch(Agent("p"), "Do you want to make this edit?\n2. No, and tell Claude"),
        ], T0);

        Assert.Equal(NoticeKind.Permission, Assert.Single(found).Kind);
    }

    [Fact]
    public void Hook_state_needs_a_live_pane_but_done_failed_and_branch_notices_do_not_depend_on_it()
    {
        var found = NoticeDetector.Detect("alpha", [
            Watch(Agent("p", status: "done"), alive: false, behind: 30) with { Hooked = Hook(AgentState.Blocked, HookStatus.PermissionReason) },
        ], T0);

        Assert.Equal([NoticeKind.Done, NoticeKind.BranchTrouble], found.Select(n => n.Kind));
    }

    [Fact]
    public void A_pane_that_was_never_seen_does_not_count_as_crashed()
    {
        Assert.Empty(NoticeDetector.Detect("alpha", [Watch(Agent(), alive: false, lost: false)], T0));
    }

    [Fact]
    public void Stalling_ignores_the_spinners_running_timer()
    {
        var before = NoticeDetector.Settled("output line\n* Churning... (12s · esc to interrupt)\n  3 tokens");
        var after = NoticeDetector.Settled("output line\n* Churning... (84s · esc to interrupt)\n  97 tokens");

        Assert.Equal(before, after);
    }

    [Fact]
    public void A_notice_opens_resolves_when_its_cause_is_gone_and_is_kept_a_day()
    {
        var opened = NoticeSync.Apply([], [Found(NoticeKind.Done)], T0);
        Assert.True(Assert.Single(opened).IsOpen);

        var resolved = NoticeSync.Apply(opened, [], T0.AddMinutes(5));
        Assert.Equal(T0.AddMinutes(5), Assert.Single(resolved).Resolved);

        Assert.Single(NoticeSync.Apply(resolved, [], T0.AddHours(23)));
        Assert.Empty(NoticeSync.Apply(resolved, [], T0.AddHours(25)));
    }

    [Fact]
    public void A_dismissed_notice_stays_dismissed_while_its_cause_lasts_and_comes_back_when_it_recurs()
    {
        var opened = NoticeSync.Apply([], [Found(NoticeKind.NeedsInput)], T0);
        var dismissed = NoticeSync.Dismiss(opened, [opened[0].Key], T0.AddMinutes(1));

        var still = NoticeSync.Apply(dismissed, [Found(NoticeKind.NeedsInput)], T0.AddMinutes(2));
        Assert.False(Assert.Single(still).IsOpen);
        Assert.Equal(T0, still[0].Since);

        var cleared = NoticeSync.Apply(still, [], T0.AddMinutes(3));
        var again = NoticeSync.Apply(cleared, [Found(NoticeKind.NeedsInput)], T0.AddMinutes(4));

        Assert.Equal(2, again.Count);
        Assert.True(again[0].IsOpen);
        Assert.Equal(T0.AddMinutes(4), again[0].Since);
    }

    [Fact]
    public void An_open_notice_keeps_its_start_and_takes_the_latest_message()
    {
        var opened = NoticeSync.Apply([], [Found(NoticeKind.Stalled) with { Message = "10 min" }], T0);
        var later = NoticeSync.Apply(opened, [Found(NoticeKind.Stalled) with { Message = "14 min" }], T0.AddMinutes(4));

        Assert.Equal((T0, "14 min"), (Assert.Single(later).Since, later[0].Message));
    }

    [Fact]
    public void Only_a_notice_that_just_opened_is_fresh_so_a_restart_or_a_dismissal_does_not_alert_again()
    {
        var first = NoticeSync.Apply([], [Found(NoticeKind.Done), Found(NoticeKind.NeedsInput, "q")], T0);
        Assert.Equal(2, NoticeSync.Fresh([], first).Count);

        var same = NoticeSync.Apply(first, [Found(NoticeKind.Done), Found(NoticeKind.NeedsInput, "q")], T0.AddMinutes(1));
        Assert.Empty(NoticeSync.Fresh(first, same));

        var dismissed = NoticeSync.Dismiss(same, [same[0].Key], T0.AddMinutes(2));
        Assert.Empty(NoticeSync.Fresh(dismissed, NoticeSync.Apply(dismissed, [Found(NoticeKind.Done), Found(NoticeKind.NeedsInput, "q")], T0.AddMinutes(3))));

        var added = NoticeSync.Apply(same, [Found(NoticeKind.Done), Found(NoticeKind.NeedsInput, "q"), Found(NoticeKind.Failed, "f")], T0.AddMinutes(4));
        Assert.Equal(NoticeKind.Failed, Assert.Single(NoticeSync.Fresh(same, added)).Kind);
    }

    [Fact]
    public void The_alert_text_names_up_to_three_agents()
    {
        var one = NoticeSync.Summary([Found(NoticeKind.Done)]);
        var five = NoticeSync.Summary([.. "abcde".Select(c => Found(NoticeKind.Done, c.ToString()))]);

        Assert.Equal("api / feat-x: m", one);
        Assert.Equal(4, five.Split('\n').Length);
        Assert.EndsWith("+2 more", five, StringComparison.Ordinal);
    }

    [Fact]
    public void The_center_shows_open_notices_per_tab_and_moves_resolved_and_dismissed_ones_to_history()
    {
        var open = Found(NoticeKind.NeedsInput, "q");
        var resolved = Found(NoticeKind.Done, "d") with { Resolved = T0.AddMinutes(5) };
        var dismissed = Found(NoticeKind.Failed, "f") with { Dismissed = T0.AddMinutes(9) };
        var elsewhere = new Notice("beta", NoticeKind.Stalled, $"{Work}/s", "api / s", "m", T0.AddMinutes(1));
        IReadOnlyDictionary<string, IReadOnlyList<Notice>> all = new Dictionary<string, IReadOnlyList<Notice>>
        {
            ["alpha"] = [open, resolved, dismissed],
            ["beta"] = [elsewhere],
        };

        Assert.Equal(["All", "alpha", "beta", "History"], Fleet.Features.Notifications.ShowNotices.NoticeTabs.Names(["alpha", "beta"]));
        Assert.Equal([elsewhere, open], Fleet.Features.Notifications.ShowNotices.NoticeTabs.For("All", all));
        Assert.Equal([open], Fleet.Features.Notifications.ShowNotices.NoticeTabs.For("alpha", all));
        Assert.Equal([dismissed, resolved], Fleet.Features.Notifications.ShowNotices.NoticeTabs.For("History", all));
        Assert.Equal("History (2)", Fleet.Features.Notifications.ShowNotices.NoticeTabs.Title("History", Fleet.Features.Notifications.ShowNotices.NoticeTabs.For("History", all)));
    }

    [Fact]
    public void The_store_keeps_notices_per_project_and_the_alert_settings()
    {
        var store = new JsonNoticeStore();
        var notices = NoticeSync.Apply([], [Found(NoticeKind.Done), Found(NoticeKind.Failed, "other")], T0);

        store.Save("alpha", notices);
        store.Save("beta", []);
        store.Save(new NoticeSettings(Bell: true, Toast: false));

        Assert.Equal(notices.Select(n => (n.Key, n.Message, n.Since)), store.Load("alpha").Select(n => (n.Key, n.Message, n.Since)));
        Assert.Equal(["alpha", "beta"], store.Projects());
        Assert.Equal(new NoticeSettings(true, false), store.Settings());
        Assert.Empty(store.Load("nobody"));
    }
}
