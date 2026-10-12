using Fleet.Features.Head.ServeHead;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Head;

public sealed partial class HeadServiceTests
{
    private readonly List<ProjectCreation> _created = [];

    private Func<ProjectCreation, Result<Project>> _creates = c => Result<Project>.Ok(new Project(c.Name, c.Path));

    private Func<Project, Task<string?>>? _openFails;

    private Result<Project> Create(ProjectCreation creation)
    {
        _created.Add(creation);
        return _creates(creation);
    }

    private static Fleet.Ports.Mcp.Models.McpRequest CreateCall(params (string Key, string Value)[] extra) =>
        Call(
            HeadTools.CreateProject,
            [(HeadTools.Name, "shop"), (HeadTools.Path, "/srv/shop"), .. extra]);

    [Fact]
    public async Task Create_project_creates_the_project_here_and_opens_it()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(CreateCall());

        Assert.False(result.IsError, result.Text);
        Assert.Equal([new ProjectCreation("shop", "/srv/shop", false)], _created);
        Assert.Contains("shop", _opened);
        Assert.Contains("created and opened", result.Text);
    }

    [Fact]
    public async Task Create_project_on_local_by_name_is_the_same_as_leaving_remote_out()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(CreateCall((HeadTools.Remote, HeadTools.Local)));

        Assert.False(result.IsError, result.Text);
        Assert.Single(_created);
    }

    [Fact]
    public async Task Create_project_passes_create_directory_through()
    {
        Open(Web, Idle);

        await Service().HandleAsync(CreateCall((HeadTools.CreateDirectory, "true")));

        Assert.True(_created.Single().CreateDirectory);
    }

    [Fact]
    public async Task Create_project_with_open_false_creates_without_opening()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(CreateCall((HeadTools.Open, "false")));

        Assert.False(result.IsError, result.Text);
        Assert.Single(_created);
        Assert.DoesNotContain("shop", _opened);
        Assert.Contains("created, not opened", result.Text);
    }

    [Fact]
    public async Task Create_project_keeps_the_project_and_names_the_reason_when_opening_fails()
    {
        Open(Web, Idle);
        _openFails = _ => Task.FromResult<string?>("no terminal");

        var result = await Service().HandleAsync(CreateCall());

        Assert.False(result.IsError, result.Text);
        Assert.Single(_created);
        Assert.Contains("created, not opened", result.Text);
        Assert.Contains("no terminal", result.Text);
    }

    [Fact]
    public async Task Create_project_returns_the_core_error_and_opens_nothing()
    {
        Open(Web, Idle);
        _creates = _ => Result<Project>.Fail("A project named 'shop' already exists.");

        var result = await Service().HandleAsync(CreateCall());

        Assert.True(result.IsError);
        Assert.Contains("already exists", result.Text);
        Assert.DoesNotContain("shop", _opened);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("path")]
    public async Task Create_project_needs_a_name_and_a_path(string missing)
    {
        Open(Web, Idle);
        var call = Call(
            HeadTools.CreateProject,
            [.. new[] { (HeadTools.Name, "shop"), (HeadTools.Path, "/srv/shop") }.Where(a => a.Item1 != missing)]);

        var result = await Service().HandleAsync(call);

        Assert.True(result.IsError);
        Assert.Contains($"'{missing}' is required", result.Text);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task Create_project_with_repositories_is_not_supported_yet()
    {
        Open(Web, Idle);

        var result = await Service().HandleAsync(CreateCall((HeadTools.Repositories, "api")));

        Assert.True(result.IsError);
        Assert.Contains("repositories are not supported yet", result.Text);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task Create_project_asks_an_open_dashboard_by_default()
    {
        Open(Web, Idle);

        await Service().HandleAsync(CreateCall());

        var asked = Assert.Single(_approvals.Asked);
        Assert.Equal("web", asked.Project);
        Assert.Equal("create_project", asked.Tool);
        Assert.Contains("shop", asked.Summary);
    }

    [Fact]
    public async Task Create_project_creates_nothing_when_the_ask_is_denied()
    {
        Open(Web, Idle);
        _approvals.Answer = ApprovalOutcome.Deny("not now");

        var result = await Service().HandleAsync(CreateCall());

        Assert.True(result.IsError);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task Create_project_creates_nothing_when_the_rule_is_forbid()
    {
        Open(Web, Idle);
        _settings.Config = SettingsConfig.Default.With(HarnessTool.CreateProject, ActionPolicy.Forbid);

        var result = await Service().HandleAsync(CreateCall());

        Assert.True(result.IsError);
        Assert.Contains("create_project", result.Text);
        Assert.Empty(_created);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task Create_project_follows_an_allow_rule_without_asking()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.CreateProject, ActionPolicy.Allow);

        var result = await Service().HandleAsync(CreateCall((HeadTools.Open, "false")));

        Assert.False(result.IsError, result.Text);
        Assert.Single(_created);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task Create_project_refuses_when_no_dashboard_is_open_to_ask()
    {
        var result = await Service().HandleAsync(CreateCall());

        Assert.True(result.IsError);
        Assert.Contains("dashboard", result.Text);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task Create_project_is_not_served_to_another_machine_in_this_unit()
    {
        var result = await Service().ServeOriginAsync(CreateCall());

        Assert.True(result.IsError);
        Assert.Empty(_created);
    }
}
