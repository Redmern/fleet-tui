using Fleet.Features.Agents.NewAgent;

namespace Fleet.Tests.Features.Agents;

public class BaseRefTests
{
    [Fact]
    public void A_local_branch_ahead_of_origin_is_preferred_so_unpushed_work_survives()
    {
        Assert.Equal("develop", BaseRef.Choose("develop", hasLocal: true, hasRemote: true, ahead: 3));
    }

    [Fact]
    public void Origin_is_used_only_when_it_is_ahead_or_equal()
    {
        Assert.Equal(
            "origin/develop",
            BaseRef.Choose("develop", hasLocal: true, hasRemote: true, ahead: 0));
    }

    [Fact]
    public void With_no_remote_the_local_branch_is_the_base()
    {
        Assert.Equal(
            "develop",
            BaseRef.Choose("develop", hasLocal: true, hasRemote: false, ahead: 0));
    }

    [Fact]
    public void With_no_local_branch_origin_is_the_base()
    {
        Assert.Equal(
            "origin/develop",
            BaseRef.Choose("develop", hasLocal: false, hasRemote: true, ahead: 0));
    }

    [Fact]
    public void The_default_branch_comes_from_origin_head_first()
    {
        Assert.Equal("develop", BaseRef.DefaultBranch("origin/develop", "master"));
    }

    [Fact]
    public void Without_origin_head_the_anchors_own_head_is_used_not_a_hardcoded_main()
    {
        Assert.Equal("master", BaseRef.DefaultBranch(string.Empty, "master"));
    }

    [Fact]
    public void Only_a_repository_with_neither_falls_back_to_main()
    {
        Assert.Equal("main", BaseRef.DefaultBranch(string.Empty, string.Empty));
    }

    [Theory]
    [InlineData("3\t0", 3)]
    [InlineData("0\t5", 0)]
    [InlineData("", 0)]
    [InlineData("not numbers", 0)]
    public void The_ahead_count_is_the_left_side_of_rev_list(string counts, int expected)
    {
        Assert.Equal(expected, BaseRef.Ahead(counts));
    }
}
