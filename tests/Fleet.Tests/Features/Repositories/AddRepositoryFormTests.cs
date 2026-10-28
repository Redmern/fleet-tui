using Fleet.Features.Repositories.AddRepository;

namespace Fleet.Tests.Features.Repositories;

public sealed class AddRepositoryFormTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_missing_name_is_an_error(string name) =>
        Assert.Equal(AddRepositoryForm.NameMissing, AddRepositoryForm.Validate(cloning: false, name, string.Empty, "main"));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Cloning_without_a_url_is_an_error(string url) =>
        Assert.Equal(AddRepositoryForm.UrlMissing, AddRepositoryForm.Validate(cloning: true, "fleet", url, "main"));

    [Fact]
    public void A_new_repository_needs_no_url() =>
        Assert.Null(AddRepositoryForm.Validate(cloning: false, "fleet", string.Empty, "main"));

    [Fact]
    public void A_missing_branch_is_an_error() =>
        Assert.Equal(AddRepositoryForm.BranchMissing, AddRepositoryForm.Validate(cloning: false, "fleet", string.Empty, " "));

    [Fact]
    public void The_name_is_reported_before_the_url() =>
        Assert.Equal(AddRepositoryForm.NameMissing, AddRepositoryForm.Validate(cloning: true, string.Empty, string.Empty, "main"));

    [Fact]
    public void A_complete_clone_is_valid() =>
        Assert.Null(AddRepositoryForm.Validate(cloning: true, "fleet", "git@x:fleet.git", "main"));
}
