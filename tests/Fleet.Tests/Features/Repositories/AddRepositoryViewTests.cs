using Fleet.Features.Repositories.AddRepository;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Repositories;

public sealed class AddRepositoryViewTests
{
    private static IReadOnlyList<FleetCardField> Fields(
        bool cloning, string name = "fleet", string url = "git@x:fleet.git", string branch = "main", List<string>? ran = null) =>
        AddRepositoryView.Fields(
            cloning,
            name,
            url,
            branch,
            () => ran?.Add("clone"),
            () => ran?.Add("name"),
            () => ran?.Add("url"),
            () => ran?.Add("branch"));

    private static IReadOnlyList<FleetPillButton> Pills() =>
        FleetCard.Pills(AddRepositoryView.Add.Icon, AddRepositoryView.Add.Label, AddRepositoryView.Add.Mnemonic, () => { }, () => { });

    [Fact]
    public void The_title_leads_with_the_add_repository_icon() =>
        Assert.Equal($"{FleetIcons.AddRepo} Add repository", AddRepositoryView.Title);

    [Fact]
    public void Every_field_leads_with_its_icon_when_cloning() =>
        Assert.Equal(
            [FleetIcons.Clone, FleetIcons.RepoName, FleetIcons.Url, FleetIcons.Branch],
            Fields(cloning: true).Select(f => f.Icon));

    [Fact]
    public void A_new_repository_has_no_url_field() =>
        Assert.Equal(["Clone", "Name", "Branch"], Fields(cloning: false).Select(f => f.Label));

    [Fact]
    public void Fields_show_their_values_and_placeholders_inline()
    {
        Assert.Equal(
            ["from a URL", "(required)", "(required)", "(required)"],
            Fields(cloning: true, string.Empty, string.Empty, string.Empty).Select(f => f.Value));
        Assert.Equal("new repository", Fields(cloning: false)[0].Value);
        Assert.Equal(
            $"{FleetIcons.Url} URL:     git@x:fleet.git",
            string.Concat(FleetCard.FieldRow(Fields(cloning: true)[2], "u").Spans.Select(s => s.Text)));
    }

    [Fact]
    public void Each_field_edits_its_own_value()
    {
        var ran = new List<string>();

        foreach (var field in Fields(cloning: true, ran: ran))
        {
            field.Edit();
        }

        Assert.Equal(["clone", "name", "url", "branch"], ran);
    }

    [Fact]
    public void Clone_name_url_and_branch_jump_on_c_n_u_and_b() =>
        Assert.Equal(["c", "n", "u", "b"], FleetCard.FieldKeys(Fields(cloning: true), Pills(), Keymap.Default));

    [Fact]
    public void Add_repository_has_an_icon_and_the_a_key() =>
        Assert.Equal((FleetIcons.AddRepo, "Add repository", "a"), AddRepositoryView.Add);

    [Fact]
    public void No_jump_key_clashes_with_the_list_motions()
    {
        Assert.All(Pills(), p => Assert.False(p.Clashes(Keymap.Default), p.Mnemonic));
        Assert.DoesNotContain("j", FleetCard.FieldKeys(Fields(cloning: true), Pills(), Keymap.Default));
        Assert.DoesNotContain("k", FleetCard.FieldKeys(Fields(cloning: true), Pills(), Keymap.Default));
    }

    [Fact]
    public void A_field_key_rebound_to_a_motion_moves_to_another_letter()
    {
        var keymap = new Keymap(KeymapConfig.Default.With(FleetAction.MoveDown, "n"));

        Assert.NotEqual("n", FleetCard.FieldKeys(Fields(cloning: true), Pills(), keymap)[1]);
    }

    [Fact]
    public void The_url_suggestions_filter_as_you_type() =>
        Assert.True(AddRepositoryView.UrlPicker.Filter);
}
