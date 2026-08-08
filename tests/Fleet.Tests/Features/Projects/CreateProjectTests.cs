using Fleet.Features.Projects.CreateProject;
using Fleet.Features.Projects.CreateProject.Enums;
using Fleet.Features.Projects.CreateProject.Models;
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

    private string AMissingRoot(string name = "missing") => Path.Combine(ConfigHome, name);

    [Fact]
    public void Creates_and_returns_the_saved_project()
    {
        var reply = Handler().Handle(new CreateProjectCommand("My Backend", ARoot()));

        Assert.Equal(CreateProjectStatus.Created, reply.Status);
        Assert.Equal("MyBackend", reply.Project!.Name);
    }

    [Fact]
    public void The_created_project_is_retrievable_afterwards()
    {
        Handler().Handle(new CreateProjectCommand("backend", ARoot()));

        Assert.NotNull(new JsonProjectStore().Load("backend"));
    }

    [Fact]
    public void Rejects_a_blank_name()
    {
        var reply = Handler().Handle(new CreateProjectCommand("  ", ARoot()));

        Assert.Equal(CreateProjectStatus.Rejected, reply.Status);
        Assert.Contains("name is required", reply.Error);
    }

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
    public void Nothing_is_saved_when_validation_fails()
    {
        Handler().Handle(new CreateProjectCommand("...", ARoot()));

        Assert.Empty(new JsonProjectStore().List());
    }

    [Fact]
    public void A_missing_root_asks_for_confirmation_rather_than_failing()
    {
        var missing = AMissingRoot();

        var reply = Handler().Handle(new CreateProjectCommand("backend", missing));

        Assert.Equal(CreateProjectStatus.NeedsRootConfirmation, reply.Status);
        Assert.Equal(Path.GetFullPath(missing), reply.RootToCreate);
        Assert.Null(reply.Error);
    }

    [Fact]
    public void Asking_for_confirmation_does_not_create_anything()
    {
        var missing = AMissingRoot();

        Handler().Handle(new CreateProjectCommand("backend", missing));

        Assert.False(Directory.Exists(missing));
        Assert.Empty(new JsonProjectStore().List());
    }

    [Fact]
    public void Confirming_creates_the_root_and_the_project()
    {
        var missing = AMissingRoot();

        var reply = Handler().Handle(
            new CreateProjectCommand("backend", missing, CreateRoot: true));

        Assert.Equal(CreateProjectStatus.Created, reply.Status);
        Assert.True(Directory.Exists(missing));
        Assert.Equal(Path.GetFullPath(missing), reply.Project!.Root);
    }

    [Fact]
    public void Confirming_creates_missing_parent_directories_too()
    {
        var nested = Path.Combine(ConfigHome, "a", "b", "c");

        var reply = Handler().Handle(new CreateProjectCommand("deep", nested, CreateRoot: true));

        Assert.Equal(CreateProjectStatus.Created, reply.Status);
        Assert.True(Directory.Exists(nested));
    }

    [Fact]
    public void An_existing_root_never_asks_for_confirmation()
        => Assert.Equal(
            CreateProjectStatus.Created,
            Handler().Handle(new CreateProjectCommand("backend", ARoot())).Status);

    [Fact]
    public void A_relative_root_is_reported_as_a_full_path_in_the_confirmation()
    {
        var reply = Handler().Handle(new CreateProjectCommand("backend", "some-relative-dir"));

        Assert.Equal(CreateProjectStatus.NeedsRootConfirmation, reply.Status);
        Assert.True(Path.IsPathFullyQualified(reply.RootToCreate!));
    }

    [Fact]
    public void A_name_is_still_validated_before_offering_to_create_a_directory()
    {
        var reply = Handler().Handle(new CreateProjectCommand("...", AMissingRoot()));

        Assert.Equal(CreateProjectStatus.Rejected, reply.Status);
    }

    [Fact]
    public void A_path_the_filesystem_refuses_is_rejected_when_creation_is_attempted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string illegal = "C:\\bad|path";

        Assert.Equal(
            CreateProjectStatus.NeedsRootConfirmation,
            Handler().Handle(new CreateProjectCommand("backend", illegal)).Status);

        var reply = Handler().Handle(
            new CreateProjectCommand("backend", illegal, CreateRoot: true));

        Assert.Equal(CreateProjectStatus.Rejected, reply.Status);
        Assert.Contains("Could not create", reply.Error);
    }
}