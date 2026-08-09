using Fleet.Features.Agents.NewAgent;

namespace Fleet.Tests.Features.Agents;

public class ListBranchesTests
{
    [Fact]
    public void Local_branches_come_before_remote_ones()
    {
        var branches = ListBranchesHandler.Parse("zeta\norigin/alpha\n");

        Assert.Equal(["zeta", "origin/alpha"], branches.Select(b => b.Reference));
    }

    [Fact]
    public void Origin_HEAD_is_not_a_branch_anyone_can_start_from()
    {
        var branches = ListBranchesHandler.Parse("main\norigin/HEAD\norigin/develop\n");

        Assert.DoesNotContain(branches, b => b.Reference == "origin/HEAD");
    }

    [Fact]
    public void A_remote_branch_already_checked_out_locally_is_not_listed_twice()
    {
        var branches = ListBranchesHandler.Parse("develop\norigin/develop\norigin/release\n");

        Assert.Equal(["develop", "origin/release"], branches.Select(b => b.Reference));
    }

    [Fact]
    public void A_remote_branch_is_labelled_so_it_is_obvious_which_it_is()
    {
        var branches = ListBranchesHandler.Parse("main\norigin/release\n");

        Assert.Equal("main", branches[0].Label);
        Assert.Equal("origin/release   (remote)", branches[1].Label);
        Assert.True(branches[1].IsRemote);
        Assert.Equal("release", branches[1].ShortName);
    }

    [Fact]
    public void No_branches_at_all_is_an_empty_list_rather_than_a_blank_entry()
    {
        Assert.Empty(ListBranchesHandler.Parse("\n  \n"));
    }
}
