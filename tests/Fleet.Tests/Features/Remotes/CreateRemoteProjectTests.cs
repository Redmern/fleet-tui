using Fleet.Features.Projects.CreateProject;
using Fleet.Features.Projects.CreateProject.Models;
using Fleet.Features.Remotes.CreateRemoteProject;
using Fleet.Features.Remotes.CreateRemoteProject.Enums;
using Fleet.Platform.Storage;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Tests.Features.Remotes;

[Collection(ConfigHomeCollection.Name)]
public sealed class CreateRemoteProjectTests : ConfigHomeFixture
{
    private const string Host = "user@homelab";

    [Fact]
    public async Task Asks_the_remote_to_create_and_open_the_project()
    {
        var remotes = new FakeRemotes(McpResult.Ok("shop created and opened; its root is /srv/shop."));

        var reply = await new CreateRemoteProjectHandler(remotes).HandleAsync(Host, " shop ", " /srv/shop ", createDirectory: false);

        Assert.Equal(RemoteProjectStatus.Created, reply.Status);
        Assert.Equal("shop", reply.Project);
        var (host, request) = Assert.Single(remotes.Calls);
        Assert.Equal(Host, host);
        Assert.Equal(new RemoteProjectRequest("shop", "/srv/shop", CreateDirectory: false, Open: true, Interactive: true), request);
    }

    [Fact]
    public async Task Names_the_project_the_remote_created_when_it_sanitized_the_name()
    {
        var remotes = new FakeRemotes(McpResult.Ok("MyShop created and opened; its root is /srv/shop."));

        var reply = await new CreateRemoteProjectHandler(remotes).HandleAsync(Host, "My Shop", "/srv/shop", createDirectory: false);

        Assert.Equal("MyShop", reply.Project);
    }

    [Theory]
    [InlineData("created created and opened; its root is /srv/shop.", "created")]
    [InlineData("shop created, not opened: the window is busy", "shop")]
    public async Task Names_the_project_from_the_full_success_wording(string text, string project)
    {
        var remotes = new FakeRemotes(McpResult.Ok(text));

        var reply = await new CreateRemoteProjectHandler(remotes).HandleAsync(Host, "typed", "/srv/shop", createDirectory: false);

        Assert.Equal(project, reply.Project);
    }

    [Fact]
    public async Task A_missing_directory_asks_to_create_it()
    {
        var remotes = new FakeRemotes(McpResult.Error("/srv/shop does not exist; nothing was created."));

        var reply = await new CreateRemoteProjectHandler(remotes).HandleAsync(Host, "shop", "/srv/shop", createDirectory: false);

        Assert.Equal(RemoteProjectStatus.NeedsDirectory, reply.Status);
        Assert.Equal("/srv/shop", reply.DirectoryToCreate);
    }

    [Fact]
    public async Task Retrying_with_create_directory_passes_it_on()
    {
        var remotes = new FakeRemotes(McpResult.Ok("shop created and opened; its root is /srv/shop."));

        await new CreateRemoteProjectHandler(remotes).HandleAsync(Host, "shop", "/srv/shop", createDirectory: true);

        Assert.True(Assert.Single(remotes.Calls).Request.CreateDirectory);
    }

    [Theory]
    [InlineData("A project named 'shop' already exists.")]
    [InlineData("homelab runs an older fleet that cannot create projects; update it.")]
    [InlineData("user@homelab is not connected")]
    public async Task Any_other_error_is_rejected_with_its_text(string error)
    {
        var remotes = new FakeRemotes(McpResult.Error(error));

        var reply = await new CreateRemoteProjectHandler(remotes).HandleAsync(Host, "shop", "/srv/shop", createDirectory: false);

        Assert.Equal(RemoteProjectStatus.Rejected, reply.Status);
        Assert.Equal(error, reply.Error);
    }

    [Theory]
    [InlineData("", "/srv/shop")]
    [InlineData("shop", "  ")]
    public async Task A_blank_name_or_path_is_rejected_without_asking_the_remote(string name, string path)
    {
        var remotes = new FakeRemotes(McpResult.Ok("unused"));

        var reply = await new CreateRemoteProjectHandler(remotes).HandleAsync(Host, name, path, createDirectory: false);

        Assert.Equal(RemoteProjectStatus.Rejected, reply.Status);
        Assert.NotNull(reply.Error);
        Assert.Empty(remotes.Calls);
    }

    [Fact]
    public async Task A_thrown_failure_is_rejected_with_its_message()
    {
        var remotes = new FakeRemotes((McpResult?)null);

        var reply = await new CreateRemoteProjectHandler(remotes).HandleAsync(Host, "shop", "/srv/shop", createDirectory: false);

        Assert.Equal(RemoteProjectStatus.Rejected, reply.Status);
        Assert.Equal("fleetd is not running", reply.Error);
    }

    [Fact]
    public async Task Submit_asks_about_a_missing_directory_and_retries_creating_it()
    {
        var remotes = new FakeRemotes(
            McpResult.Error("/srv/shop does not exist; nothing was created."),
            McpResult.Ok("shop created and opened; its root is /srv/shop."));
        string? asked = null;

        var reply = await new CreateRemoteProjectHandler(remotes).SubmitAsync(
            Host, "shop", "/srv/shop", dir => { asked = dir; return Task.FromResult(true); });

        Assert.Equal("/srv/shop", asked);
        Assert.Equal(RemoteProjectStatus.Created, reply.Status);
        Assert.Equal([false, true], remotes.Calls.Select(c => c.Request.CreateDirectory));
    }

    [Fact]
    public async Task Submit_declining_the_directory_creates_nothing()
    {
        var remotes = new FakeRemotes(McpResult.Error("/srv/shop does not exist; nothing was created."));

        var reply = await new CreateRemoteProjectHandler(remotes).SubmitAsync(
            Host, "shop", "/srv/shop", _ => Task.FromResult(false));

        Assert.Equal(RemoteProjectStatus.Rejected, reply.Status);
        Assert.Equal(CreateRemoteProjectHandler.DirectoryDeclined, reply.Error);
        Assert.Single(remotes.Calls);
    }

    [Fact]
    public async Task Submit_does_not_ask_when_the_directory_exists()
    {
        var remotes = new FakeRemotes(McpResult.Ok("shop created and opened; its root is /srv/shop."));
        var asked = false;

        var reply = await new CreateRemoteProjectHandler(remotes).SubmitAsync(
            Host, "shop", "/srv/shop", _ => { asked = true; return Task.FromResult(true); });

        Assert.False(asked);
        Assert.Equal("shop", reply.Project);
    }

    [Fact]
    public void The_missing_directory_suffix_matches_what_create_project_answers()
    {
        var missing = Path.Combine(ConfigHome, "missing");

        var created = new CreateProjectHandler(new JsonProjectStore()).Create(new CreateProjectCommand("shop", missing));

        Assert.False(created.Succeeded);
        Assert.EndsWith(CreateRemoteProjectHandler.MissingDirectorySuffix, created.Error);
        Assert.Equal(missing, created.Error![..^CreateRemoteProjectHandler.MissingDirectorySuffix.Length]);
    }

    private sealed class FakeRemotes(params McpResult?[] answers) : IRemoteMachines
    {
        public List<(string Host, RemoteProjectRequest Request)> Calls { get; } = [];

        public Task<McpResult> CreateProjectAsync(string host, RemoteProjectRequest request, CancellationToken ct = default)
        {
            Calls.Add((host, request));
            var answer = answers[Math.Min(Calls.Count, answers.Length) - 1];
            return answer is null
                ? throw new Fleet.Ports.Mux.Exceptions.MuxUnavailableException("fleetd is not running")
                : Task.FromResult(answer);
        }

        public Task<IReadOnlyList<RemoteMachine>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RemoteMachine>>([]);

        public Task ConnectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task AnswerAsync(string host, string answer, CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task OpenInNewWindowAsync(string host, string project, CancellationToken ct = default) => Task.CompletedTask;

        public Task ShowHereAsync(string host, string project, CancellationToken ct = default) => Task.CompletedTask;

        public Task NewProjectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task<McpResult> HeadAsync(string host, McpRequest request, CancellationToken ct = default) => Task.FromResult(McpResult.Ok(string.Empty));
    }
}
