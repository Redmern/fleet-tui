using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Shared.Results;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Agents;

public sealed class NewAgentViewTests
{
    [Fact]
    public void The_title_leads_with_the_new_agent_icon() =>
        Assert.Equal($"{FleetIcons.NewAgent} New agent", NewAgentView.Title);

    private static IReadOnlyList<FleetCardField> Fields(List<string>? ran = null) =>
        NewAgentView.Fields(
            "fleet",
            "feat/x",
            "main",
            "claude",
            () => ran?.Add("repo"),
            () => ran?.Add("branch"),
            () => ran?.Add("base"),
            () => ran?.Add("opens"));

    [Fact]
    public void Every_field_leads_with_its_icon()
    {
        Assert.Equal(
            [FleetIcons.Repo, FleetIcons.Branch, FleetIcons.Base, FleetIcons.Harness],
            Fields().Select(f => f.Icon));
    }

    [Fact]
    public void A_field_shows_its_label_and_inline_value()
    {
        var row = FleetCard.FieldRow(Fields()[1], "b");

        Assert.Equal($"{FleetIcons.Branch} Branch:  feat/x", string.Concat(row.Spans.Select(s => s.Text)));
    }

    [Fact]
    public void Repo_branch_base_and_opens_jump_on_r_b_a_and_o()
    {
        var keys = FleetCard.FieldKeys(Fields(), Pills(), Keymap.Default);

        Assert.Equal(["r", "b", "a", "o"], keys);
    }

    [Fact]
    public void Each_field_edits_its_own_value()
    {
        var ran = new List<string>();

        foreach (var field in Fields(ran))
        {
            field.Edit();
        }

        Assert.Equal(["repo", "branch", "base", "opens"], ran);
    }

    private static IReadOnlyList<FleetPillButton> Pills() =>
        FleetCard.Pills(NewAgentView.Create.Icon, NewAgentView.Create.Label, NewAgentView.Create.Mnemonic, () => { }, () => { });

    [Fact]
    public void Create_agent_has_an_icon_and_the_c_key()
    {
        Assert.Equal((FleetIcons.Create, "Create agent", "c"), NewAgentView.Create);
    }

    [Fact]
    public void No_jump_key_clashes_with_the_list_motions()
    {
        foreach (var pill in Pills())
        {
            Assert.False(pill.Clashes(Keymap.Default), pill.Mnemonic);
        }

        Assert.DoesNotContain("j", FleetCard.FieldKeys(Fields(), Pills(), Keymap.Default));
        Assert.DoesNotContain("k", FleetCard.FieldKeys(Fields(), Pills(), Keymap.Default));
    }

    [Fact]
    public void A_field_key_rebound_to_a_motion_moves_to_another_letter()
    {
        var keymap = new Keymap(KeymapConfig.Default.With(FleetAction.MoveDown, "r"));

        Assert.NotEqual("r", FleetCard.FieldKeys(Fields(), Pills(), keymap)[0]);
    }

    [Fact]
    public void The_base_and_repository_pickers_filter_as_you_type()
    {
        Assert.True(NewAgentView.RepositoryPicker.Filter);
        Assert.True(NewAgentView.BasePicker(null, "/r", new()).Filter);
    }

    [Fact]
    public void Without_a_fetcher_the_base_picker_does_not_refresh()
    {
        Assert.Null(NewAgentView.BasePicker(null, "/r", new()).Refresh);
    }

    [Fact]
    public void Opening_the_base_picker_fetches_the_chosen_repository()
    {
        var asked = new List<string>();
        var options = NewAgentView.BasePicker(
            (directory, _) =>
            {
                asked.Add(directory);
                return Task.FromResult(Result<IReadOnlyList<BranchChoice>>.Ok([]));
            },
            "/r/backend",
            new());

        Assert.NotNull(options.Refresh);
        Assert.Equal("fetching origin...", options.Refreshing);
        options.Refresh!(CancellationToken.None);
        Assert.Equal(["/r/backend"], asked);
    }

    [Fact]
    public async Task Fetched_branches_become_picker_entries_and_can_be_chosen()
    {
        var known = new System.Collections.Concurrent.ConcurrentDictionary<string, BranchChoice>();
        var options = NewAgentView.BasePicker(
            (_, _) => Task.FromResult(Result<IReadOnlyList<BranchChoice>>.Ok(
                [new BranchChoice("main", false), new BranchChoice("origin/feat/new", true)])),
            "/r",
            known);

        var refreshed = await options.Refresh!(CancellationToken.None);

        Assert.Equal(["main", new BranchChoice("origin/feat/new", true).Label], refreshed.Entries.Select(e => e.Label));
        Assert.Equal("origin/feat/new", known[refreshed.Entries[1].Label].Reference);
        Assert.Equal(string.Empty, refreshed.Note);
    }

    [Fact]
    public async Task A_failed_fetch_keeps_the_list_and_leaves_a_note()
    {
        var options = NewAgentView.BasePicker(
            (_, _) => Task.FromResult(Result<IReadOnlyList<BranchChoice>>.Fail("offline: could not resolve host")),
            "/r",
            new());

        var refreshed = await options.Refresh!(CancellationToken.None);

        Assert.Empty(refreshed.Entries);
        Assert.Equal("offline: could not resolve host", refreshed.Note);
    }

    [Fact]
    public void A_cached_branch_maps_back_to_its_reference()
    {
        var known = NewAgentView.Known([new BranchChoice("origin/x", true)]);

        Assert.Equal("origin/x", known[new BranchChoice("origin/x", true).Label].Reference);
    }

    private static NewAgentPrompt Prompt(int selected = 0, string harness = AgentHarness.Nvim) =>
        new(
            "techweb",
            [("backend", "/r/backend", "main"), ("frontend", "/r/frontend", "main")],
            selected,
            _ => [],
            harness);

    [Fact]
    public void The_last_used_repository_and_harness_are_preselected()
    {
        var picked = NewAgentView.Preselect(Prompt(), "frontend", AgentHarness.Claude);

        Assert.Equal((1, AgentHarness.Claude), picked);
    }

    [Fact]
    public void A_last_repository_that_is_gone_keeps_the_prompt_selection()
    {
        var picked = NewAgentView.Preselect(Prompt(selected: 1), "removed", AgentHarness.Claude);

        Assert.Equal((1, AgentHarness.Claude), picked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("vscode")]
    [InlineData(AgentHarness.Orchestrator)]
    public void A_harness_a_new_agent_cannot_open_keeps_the_prompt_harness(string last)
    {
        var picked = NewAgentView.Preselect(Prompt(), "backend", last);

        Assert.Equal((0, AgentHarness.Nvim), picked);
    }

    [Fact]
    public void A_remembered_nvim_harness_gives_way_to_a_bare_claude_default()
    {
        var picked = NewAgentView.Preselect(Prompt(harness: AgentHarness.Claude), "backend", AgentHarness.Nvim);

        Assert.Equal((0, AgentHarness.Claude), picked);
    }

    [Fact]
    public void A_remembered_claude_harness_still_wins_over_an_nvim_default()
    {
        var picked = NewAgentView.Preselect(Prompt(), "backend", AgentHarness.Claude);

        Assert.Equal((0, AgentHarness.Claude), picked);
    }

    [Fact]
    public void Nothing_remembered_keeps_the_prompt()
    {
        var picked = NewAgentView.Preselect(Prompt(selected: 1, harness: AgentHarness.Claude), string.Empty, string.Empty);

        Assert.Equal((1, AgentHarness.Claude), picked);
    }
}
