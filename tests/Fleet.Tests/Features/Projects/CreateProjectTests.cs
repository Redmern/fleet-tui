using Fleet.Features.Projects.CreateProject;
using Fleet.Platform.Storage;

namespace Fleet.Tests.Features.Projects;

[Collection(ConfigHomeCollection.Name)]
public sealed class CreateProjectTests : ConfigHomeFixture
{
    private CreateProjectHandler Handler() => new(new JsonProjectStore());

    private string ARoot(string name = "root")
    {
        var dir = Path.Combine(ConfigHome, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Creates_and_returns_the_saved_project()
    {
        var result = Handler().Handle(new CreateProjectCommand("My Backend", ARoot()));

        Assert.True(result.Succeeded);
        Assert.Equal("MyBackend", result.Value.Name);
    }

    [Fact]
    public void The_created_project_is_retrievable_afterwards()
    {
        Handler().Handle(new CreateProjectCommand("backend", ARoot()));

        Assert.NotNull(new JsonProjectStore().Load("backend"));
    }

    [Fact]
    public void Rejects_a_blank_name()
        => Assert.Contains(
            "name is required",
            Handler().Handle(new CreateProjectCommand("  ", ARoot())).Error);

    [Fact]
    public void Rejects_a_blank_root()
        => Assert.Contains(
            "root directory is required",
            Handler().Handle(new CreateProjectCommand("x", "  ")).Error);

    [Fact]
    public void Rejects_a_name_with_no_usable_characters()
        => Assert.Contains(
            "no usable characters",
            Handler().Handle(new CreateProjectCommand("...", ARoot())).Error);

    [Fact]
    public void Rejects_a_root_that_does_not_exist()
        => Assert.Contains(
            "does not exist",
            Handler().Handle(
                new CreateProjectCommand("x", Path.Combine(ConfigHome, "nope"))).Error);

    [Fact]
    public void Nothing_is_saved_when_validation_fails()
    {
        Handler().Handle(new CreateProjectCommand("...", ARoot()));

        Assert.Empty(new JsonProjectStore().List());
    }
}
