using Fleet.Features.Repositories.RemoveRepository;

namespace Fleet.Tests.Features.Repositories;

public class RepositoryContentsTests
{
    [Fact]
    public void Every_worktree_path_is_pulled_out_of_the_porcelain()
    {
        var listing = string.Join(
            '\n',
            "worktree C:/repos/techweb/backend",
            "bare",
            string.Empty,
            "worktree C:/repos/techweb/backend/develop",
            "HEAD f07f6ec",
            "branch refs/heads/develop");

        Assert.Equal(
            ["C:/repos/techweb/backend", "C:/repos/techweb/backend/develop"],
            RepositoryContents.Worktrees(listing));
    }

    [Fact]
    public void No_worktrees_is_an_empty_list()
    {
        Assert.Empty(RepositoryContents.Worktrees(string.Empty));
    }

    [Fact]
    public void A_branch_ahead_of_its_upstream_is_unpushed()
    {
        Assert.Equal(["feature/login"], RepositoryContents.Unpushed("feature/login|refs/remotes/origin/feature/login|[ahead 2]\n"));
    }

    [Fact]
    public void A_branch_with_no_upstream_at_all_is_unpushed()
    {
        Assert.Equal(["local-only"], RepositoryContents.Unpushed("local-only||\n"));
    }

    [Fact]
    public void A_branch_whose_upstream_is_gone_is_unpushed()
    {
        Assert.Equal(["stale"], RepositoryContents.Unpushed("stale|refs/remotes/origin/stale|[gone]\n"));
    }

    [Fact]
    public void A_branch_level_with_its_upstream_is_not_unpushed()
    {
        Assert.Empty(RepositoryContents.Unpushed("develop|refs/remotes/origin/develop|\n"));
    }

    [Fact]
    public void A_branch_only_behind_is_not_unpushed()
    {
        Assert.Empty(RepositoryContents.Unpushed("develop|refs/remotes/origin/develop|[behind 3]\n"));
    }
}
