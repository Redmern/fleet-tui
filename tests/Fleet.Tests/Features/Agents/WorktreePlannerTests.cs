using Fleet.Features.Agents.NewAgent;

namespace Fleet.Tests.Features.Agents;

public class WorktreePlannerTests
{
    private static readonly Func<string, bool> NothingExists = _ => false;

    [Fact]
    public void A_bare_repository_plans_a_worktree_beside_its_siblings()
    {
        var plan = WorktreePlanner.For(
            Path.Combine("C:", "repos", "techweb", "backend"), "develop", true, NothingExists);

        Assert.Equal(
            Path.Combine("C:", "repos", "techweb", "backend", "develop"), plan.TargetDirectory);

        Assert.True(plan.MustCreate);
        Assert.Equal(Path.Combine("C:", "repos", "techweb", "backend"), plan.Anchor);
    }

    [Fact]
    public void A_branch_with_a_slash_is_slugged_into_a_directory_name()
    {
        var plan = WorktreePlanner.For("base", "feature/login", true, NothingExists);

        Assert.Equal(Path.Combine("base", "feature_login"), plan.TargetDirectory);
    }

    [Fact]
    public void An_existing_worktree_is_reused_rather_than_recreated()
    {
        var plan = WorktreePlanner.For("base", "develop", true, _ => true);

        Assert.False(plan.MustCreate);
        Assert.Equal(Path.Combine("base", "develop"), plan.TargetDirectory);
    }

    [Fact]
    public void A_leftover_directory_that_is_not_a_worktree_is_still_created()
    {
        var plan = WorktreePlanner.For("base", "test2", true, _ => false);

        Assert.True(plan.MustCreate);
    }

    [Fact]
    public void Only_a_directory_holding_dot_git_counts_as_a_worktree()
    {
        var root = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
        var bare = Path.Combine(root, "empty");
        var real = Path.Combine(root, "real");

        try
        {
            Directory.CreateDirectory(bare);
            Directory.CreateDirectory(real);
            File.WriteAllText(Path.Combine(real, ".git"), "gitdir: ../x");

            Assert.False(WorktreePlanner.LooksLikeWorktree(bare));
            Assert.True(WorktreePlanner.LooksLikeWorktree(real));
            Assert.False(WorktreePlanner.LooksLikeWorktree(Path.Combine(root, "missing")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void A_plain_repository_plans_to_itself_with_nothing_to_create()
    {
        var plan = WorktreePlanner.For("base", "develop", false, NothingExists);

        Assert.Equal("base", plan.TargetDirectory);
        Assert.False(plan.MustCreate);
        Assert.Equal("base", plan.Anchor);
    }
}
