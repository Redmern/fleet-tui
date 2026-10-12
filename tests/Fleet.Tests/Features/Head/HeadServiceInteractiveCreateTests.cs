using Fleet.Features.Head.ServeHead;
using Fleet.Ports.Approvals.Models;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Remotes.Models;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Head;

public sealed partial class HeadServiceTests
{
    [Fact]
    public async Task An_interactive_create_from_another_machine_needs_no_approval_or_open_project()
    {
        var result = await Service().ServeCreateProjectAsync(
            new RemoteProjectRequest("shop", "/srv/shop", false, true, Interactive: true));

        Assert.False(result.IsError, result.Text);
        Assert.Single(_created);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task An_interactive_create_skips_the_ask_even_with_a_dashboard_open()
    {
        Open(Web, Idle);
        _approvals.Answer = ApprovalOutcome.Deny("not now");

        var result = await Service().ServeCreateProjectAsync(
            new RemoteProjectRequest("shop", "/srv/shop", false, true, Interactive: true));

        Assert.False(result.IsError, result.Text);
        Assert.Empty(_approvals.Asked);
        Assert.Contains("shop", _opened);
    }

    [Fact]
    public async Task An_interactive_create_still_returns_the_core_error()
    {
        _creates = _ => Result<Project>.Fail("A project named 'shop' already exists.");

        var result = await Service().ServeCreateProjectAsync(
            new RemoteProjectRequest("shop", "/srv/shop", false, true, Interactive: true));

        Assert.True(result.IsError);
        Assert.Contains("already exists", result.Text);
    }

    [Fact]
    public async Task A_non_interactive_create_from_another_machine_is_still_asked()
    {
        Open(Web, Idle);

        await Service().ServeCreateProjectAsync(new RemoteProjectRequest("shop", "/srv/shop", false, true));

        Assert.Equal("create_project", Assert.Single(_approvals.Asked).Tool);
    }

    [Fact]
    public async Task The_head_tool_cannot_make_a_local_create_interactive()
    {
        var result = await Service().HandleAsync(CreateCall(("interactive", "true")));

        Assert.True(result.IsError);
        Assert.Contains("no project is open", result.Text);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task The_head_tool_never_sends_an_interactive_create_to_a_remote()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        await Service().HandleAsync(CreateCall((HeadTools.Remote, "hostinger"), ("interactive", "true")));

        Assert.False(Assert.Single(_remotes.Creations).Request.Interactive);
    }

    [Fact]
    public void Interactive_is_not_a_head_tool_parameter()
    {
        var create = HeadTools.All.Single(t => t.Name == HeadTools.CreateProject);

        Assert.DoesNotContain(create.Params, p => p.Name.Contains("interactive", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task An_interactive_create_still_honours_a_forbid_rule()
    {
        Open(Web, Idle);
        _settings.Config = SettingsConfig.Default.With(HarnessTool.CreateProject, ActionPolicy.Forbid);

        var result = await Service().ServeCreateProjectAsync(
            new RemoteProjectRequest("shop", "/srv/shop", false, true, Interactive: true));

        Assert.True(result.IsError);
        Assert.Equal("fleet does not allow create_project.", result.Text);
        Assert.Empty(_created);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task An_interactive_create_follows_an_allow_rule()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.CreateProject, ActionPolicy.Allow);

        var result = await Service().ServeCreateProjectAsync(
            new RemoteProjectRequest("shop", "/srv/shop", false, true, Interactive: true));

        Assert.False(result.IsError, result.Text);
        Assert.Single(_created);
    }

    [Fact]
    public async Task A_non_interactive_create_still_honours_a_forbid_rule()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.CreateProject, ActionPolicy.Forbid);

        var result = await Service().ServeCreateProjectAsync(new RemoteProjectRequest("shop", "/srv/shop", false, true));

        Assert.True(result.IsError);
        Assert.Equal("fleet does not allow create_project.", result.Text);
    }
}
