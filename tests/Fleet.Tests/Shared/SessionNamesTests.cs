using Fleet.Shared;

namespace Fleet.Tests.Shared;

public sealed class SessionNamesTests
{
    [Fact]
    public void Each_role_gets_its_own_name()
    {
        Assert.Equal("fleet-head", SessionNames.Head);
        Assert.Equal("techweb-main", SessionNames.MainOrchestrator("techweb"));
        Assert.Equal("techweb-sub-add-oauth-login", SessionNames.SubOrchestrator("techweb", "add-oauth-login"));
        Assert.Equal("techweb-backend-main", SessionNames.RepoAgent("techweb", "backend", "main"));
    }

    [Theory]
    [InlineData("feat/session-names", "techweb-api-feat-session-names")]
    [InlineData("feature\\login", "techweb-api-feature-login")]
    [InlineData("fix/a.b c", "techweb-api-fix-a-b-c")]
    [InlineData("release//v1.2", "techweb-api-release-v1-2")]
    [InlineData("snake_case", "techweb-api-snake_case")]
    public void A_branch_is_turned_into_letters_digits_hyphens_and_underscores(string branch, string expected)
    {
        Assert.Equal(expected, SessionNames.RepoAgent("techweb", "api", branch));
    }

    [Theory]
    [InlineData("techweb")]
    [InlineData("feat/x")]
    [InlineData(" a b ")]
    [InlineData("--x--")]
    [InlineData("ünïcode/ß")]
    public void A_part_only_ever_holds_characters_an_at_mention_accepts(string text)
    {
        var part = SessionNames.Part(text);

        Assert.All(part, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_', $"'{c}' in {part}"));
        Assert.False(part.StartsWith('-'));
        Assert.False(part.EndsWith('-'));
        Assert.DoesNotContain("--", part);
    }

    [Fact]
    public void An_empty_part_is_left_out_instead_of_leaving_a_double_hyphen()
    {
        Assert.Equal("techweb-feat-x", SessionNames.RepoAgent("techweb", string.Empty, "feat/x"));
        Assert.Equal("techweb-sub-x", SessionNames.SubOrchestrator("techweb", "/x/"));
    }

    [Fact]
    public void An_orchestrator_record_is_named_as_a_sub_orchestrator_whatever_its_repository()
    {
        Assert.Equal("techweb-sub-slug", SessionNames.ForAgent("techweb", string.Empty, "slug", orchestrator: true));
        Assert.Equal("techweb-sub-slug", SessionNames.ForAgent("techweb", "orchestrations", "slug", orchestrator: true));
        Assert.Equal("techweb-api-feat-x", SessionNames.ForAgent("techweb", "api", "feat/x", orchestrator: false));
    }

    [Fact]
    public void The_same_inputs_always_give_the_same_name()
    {
        Assert.Equal(
            SessionNames.RepoAgent("techweb", "api", "feat/x"),
            SessionNames.RepoAgent("techweb", "api", "feat/x"));
    }
}
