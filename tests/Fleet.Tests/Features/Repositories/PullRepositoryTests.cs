using Fleet.Features.Repositories;
using Fleet.Features.Repositories.PullRepository;

namespace Fleet.Tests.Features.Repositories;

public class PullRepositoryTests
{
    [Fact]
    public void No_output_from_a_fast_forward_means_nothing_changed()
    {
        Assert.Equal(PullOutcome.AlreadyCurrent, PullOutcome.Summarise("   \n"));
    }

    [Fact]
    public void Git_saying_it_is_current_is_normalised()
    {
        Assert.Equal(PullOutcome.AlreadyCurrent, PullOutcome.Summarise("Already up to date."));
    }

    [Fact]
    public void Only_the_first_line_of_a_merge_is_reported()
    {
        var summary = PullOutcome.Summarise("Updating abc123..def456\nFast-forward\n 3 files changed");

        Assert.Equal("Updating abc123..def456", summary);
    }

    [Fact]
    public void A_very_long_line_is_trimmed_so_it_fits_the_status_row()
    {
        Assert.Equal(90, PullOutcome.Summarise(new string('x', 200)).Length);
    }

    [Fact]
    public void A_repository_worktree_is_the_default_branch_checkout_when_it_exists()
    {
        var directory = RepositoryWorktree.For("base", "feature/login", _ => true);

        Assert.Equal(Path.Combine("base", "feature_login"), directory);
    }

    [Fact]
    public void Without_that_checkout_the_container_itself_is_used()
    {
        Assert.Equal("base", RepositoryWorktree.For("base", "develop", _ => false));
    }
}
