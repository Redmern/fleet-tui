using Fleet.Features.Agents.NewAgent;

namespace Fleet.Tests.Features.Agents;

public class ListBranchesTests
{
    [Fact]
    public void Local_branches_come_before_remote_ones()
    {
        var branches = ListBranchesHandler.Parse(
            "refs/heads/zeta\nrefs/remotes/origin/alpha\n");

        Assert.Equal(["zeta", "origin/alpha"], branches.Select(b => b.Reference));
    }

    [Fact]
    public void The_remotes_own_HEAD_is_not_a_branch_anyone_can_start_from()
    {
        var branches = ListBranchesHandler.Parse(
            "refs/heads/main\nrefs/remotes/origin/HEAD\nrefs/remotes/origin/develop\n");

        Assert.Equal(["main", "origin/develop"], branches.Select(b => b.Reference));
    }

    [Fact]
    public void The_remote_HEAD_never_appears_as_a_branch_called_origin()
    {
        var branches = ListBranchesHandler.Parse(
            "refs/heads/main\nrefs/remotes/origin/HEAD\n");

        Assert.DoesNotContain(branches, b => b.Reference == "origin");
    }

    [Fact]
    public void A_remote_branch_already_checked_out_locally_is_not_listed_twice()
    {
        var branches = ListBranchesHandler.Parse(
            "refs/heads/develop\nrefs/remotes/origin/develop\nrefs/remotes/origin/release\n");

        Assert.Equal(["develop", "origin/release"], branches.Select(b => b.Reference));
    }

    [Fact]
    public void A_branch_whose_name_contains_slashes_survives_intact()
    {
        var branches = ListBranchesHandler.Parse(
            "refs/heads/feature/RTW-212-filter\nrefs/remotes/origin/fix/RTW-407\n");

        Assert.Equal(["feature/RTW-212-filter", "origin/fix/RTW-407"],
            branches.Select(b => b.Reference));

        Assert.Equal("fix/RTW-407", branches[1].ShortName);
    }

    [Fact]
    public void A_remote_branch_is_labelled_so_it_is_obvious_which_it_is()
    {
        var branches = ListBranchesHandler.Parse(
            "refs/heads/main\nrefs/remotes/origin/release\n");

        Assert.Equal("main", branches[0].Label);
        Assert.Equal("origin/release   (remote)", branches[1].Label);
        Assert.True(branches[1].IsRemote);
    }

    [Fact]
    public void Tags_and_other_refs_are_ignored()
    {
        var branches = ListBranchesHandler.Parse(
            "refs/tags/v1.0\nrefs/heads/main\nrefs/stash\n");

        Assert.Equal(["main"], branches.Select(b => b.Reference));
    }

    [Fact]
    public void No_branches_at_all_is_an_empty_list_rather_than_a_blank_entry()
    {
        Assert.Empty(ListBranchesHandler.Parse("\n  \n"));
    }
}
