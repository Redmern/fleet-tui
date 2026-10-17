using Fleet.Features.Head.ServeHead;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Remotes.Models;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Head;

public sealed partial class HeadServiceTests
{
    private readonly Projects _projects = new(Web, Api);

    private readonly List<(string Project, string Repository, bool WasOpen)> _added = [];

    private readonly List<string> _closed = [];

    private Func<string, Result> _adds = _ => Result.Ok();

    private bool _addsWithoutDep;

    private string? _closeFails;

    private Task<Result> AddRepository(Project project, string repository, CancellationToken ct)
    {
        _added.Add((project.Name, repository, _opened.Contains(project.Name)));
        return Task.FromResult(_adds(repository));
    }

    private bool _closeThrows;

    private Task<string?> Close(Project project, CancellationToken ct)
    {
        _closed.Add(project.Name);
        return _closeThrows ? throw new IOException("mux gone") : Task.FromResult(_closeFails);
    }

    [Fact]
    public async Task Create_project_adds_each_repository_after_opening_it()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api, web")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal([("shop", "api", true), ("shop", "web", true)], _added);
        Assert.Contains("created and opened", result.Text);
        Assert.Contains("api", result.Text);
        Assert.Contains("web", result.Text);
        Assert.Empty(_projects.Removed);
    }

    [Fact]
    public async Task Create_project_names_the_repositories_when_it_asks()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.CreateProject, ActionPolicy.Ask);
        Open(Web, Idle);

        await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api,web")));

        Assert.Contains("api, web", Assert.Single(_approvals.Asked).Summary);
    }

    [Fact]
    public async Task Create_project_skips_blank_repository_entries()
    {
        Open(Web, Idle);

        await Service().HandleAsync(CreateCall((HeadTools.Repositories, " api, ,, web ,")));

        Assert.Equal(["api", "web"], _added.Select(a => a.Repository));
    }

    [Fact]
    public async Task Create_project_removes_the_project_and_names_the_repository_that_failed()
    {
        Open(Web, Idle);
        _adds = r => r == "billing" ? Result.Fail("git clone: no such repository") : Result.Ok();

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api,billing,web")));

        Assert.True(result.IsError);
        Assert.Contains("billing", result.Text);
        Assert.Contains("no such repository", result.Text);
        Assert.Contains("/srv/shop", result.Text);
        Assert.Equal(["api", "billing"], _added.Select(a => a.Repository));
        Assert.Equal(["shop"], _projects.Removed);
        Assert.Equal(["shop"], _closed);
    }

    [Fact]
    public async Task Create_project_says_when_the_rolled_back_project_could_not_be_closed()
    {
        Open(Web, Idle);
        _adds = _ => Result.Fail("boom");
        _closeFails = "no pane";

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.True(result.IsError);
        Assert.Contains("no pane", result.Text);
        Assert.Equal(["shop"], _projects.Removed);
    }

    [Fact]
    public async Task Create_project_removes_the_project_when_it_cannot_open_it_to_add_repositories()
    {
        Open(Web, Idle);
        _openFails = _ => Task.FromResult<string?>("no terminal");

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.True(result.IsError);
        Assert.Contains("no terminal", result.Text);
        Assert.Empty(_added);
        Assert.Equal(["shop"], _projects.Removed);
        Assert.Equal(["shop"], _closed);
    }

    [Fact]
    public async Task Create_project_rejects_repositories_without_opening_before_creating_anything()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(
            CreateCall((HeadTools.Repositories, "api"), (HeadTools.Open, "false")));

        Assert.True(result.IsError);
        Assert.Contains(HeadTools.Repositories, result.Text);
        Assert.Contains(HeadTools.Open, result.Text);
        Assert.Empty(_created);
        Assert.Empty(_approvals.Asked);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Create_project_does_not_add_repositories_when_creation_fails()
    {
        Open(Web, Idle);
        _creates = _ => Result<Project>.Fail("A project named 'shop' already exists.");

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.True(result.IsError);
        Assert.Empty(_added);
        Assert.Empty(_projects.Removed);
    }

    [Fact]
    public async Task Create_project_on_a_remote_sends_the_repositories_along()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        var result = await Service().HandleAsync(
            CreateCall((HeadTools.Remote, "hostinger"), (HeadTools.Repositories, "api, web")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(["api", "web"], Assert.Single(_remotes.Creations).Request.Repositories ?? []);
        Assert.Empty(_added);
    }

    [Fact]
    public async Task Create_project_on_a_remote_rejects_repositories_without_opening_before_sending()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        var result = await Service().HandleAsync(CreateCall(
            (HeadTools.Remote, "hostinger"), (HeadTools.Repositories, "api"), (HeadTools.Open, "false")));

        Assert.True(result.IsError);
        Assert.Empty(_remotes.Creations);
    }

    [Fact]
    public async Task Create_project_served_from_the_origin_adds_the_repositories_here()
    {
        Open(Web, Idle);

        var result = await Service().ServeCreateProjectAsync(
            new RemoteProjectRequest("shop", "/srv/shop", false, true, ["api", "web"]));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(["api", "web"], _added.Select(a => a.Repository));
    }

    [Fact]
    public async Task Create_project_served_from_the_origin_rolls_back_here_too()
    {
        Open(Web, Idle);
        _adds = _ => Result.Fail("boom");

        var result = await Service().ServeCreateProjectAsync(
            new RemoteProjectRequest("shop", "/srv/shop", false, true, ["api"]));

        Assert.True(result.IsError);
        Assert.Equal(["shop"], _projects.Removed);
    }

    [Fact]
    public async Task Create_project_says_the_project_was_removed_and_the_root_left_in_place()
    {
        Open(Web, Idle);
        _adds = _ => Result.Fail("boom");

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.Contains("Removed the project shop again", result.Text);
        Assert.Contains("/srv/shop and anything added there were left in place", result.Text);
    }

    [Fact]
    public async Task Create_project_says_when_the_rolled_back_project_file_could_not_be_removed()
    {
        Open(Web, Idle);
        _adds = _ => Result.Fail("boom");
        _projects.Stuck = true;

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.True(result.IsError);
        Assert.Contains("Could not remove the project shop", result.Text);
    }

    [Fact]
    public async Task Create_project_rolls_back_when_adding_a_repository_throws()
    {
        Open(Web, Idle);
        _adds = _ => throw new IOException("disk full");

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.True(result.IsError);
        Assert.Contains("api", result.Text);
        Assert.Contains("disk full", result.Text);
        Assert.Equal(["shop"], _projects.Removed);
        Assert.Equal(["shop"], _closed);
    }

    [Fact]
    public async Task Create_project_rolls_back_when_adding_a_repository_is_cancelled()
    {
        Open(Web, Idle);
        _adds = _ => throw new OperationCanceledException();

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.True(result.IsError);
        Assert.Equal(["shop"], _projects.Removed);
        Assert.Equal(["shop"], _closed);
    }

    [Fact]
    public async Task Create_project_closes_what_it_partly_opened_when_opening_fails()
    {
        Open(Web, Idle);
        _openFails = _ => Task.FromResult<string?>("no terminal.");

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.Equal(["shop"], _closed);
        Assert.DoesNotContain("..", result.Text);
    }

    [Fact]
    public async Task Create_project_adds_a_repository_named_twice_once()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api,API,web")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal(["api", "web"], _added.Select(a => a.Repository));
    }

    [Fact]
    public async Task Create_project_refuses_repositories_before_creating_when_this_fleet_cannot_add_them()
    {
        Open(Web, Idle);
        _addsWithoutDep = true;

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.True(result.IsError);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task Create_project_still_removes_the_project_when_closing_it_throws()
    {
        Open(Web, Idle);
        _adds = _ => Result.Fail("boom");
        _closeThrows = true;

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.True(result.IsError);
        Assert.Contains("mux gone", result.Text);
        Assert.Equal(["shop"], _projects.Removed);
    }
}
