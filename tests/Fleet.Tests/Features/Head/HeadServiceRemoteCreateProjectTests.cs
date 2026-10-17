using Fleet.Features.Head.ServeHead;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;
using Fleet.Shared.Iso;
using Fleet.Shared.Iso.Models;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Head;

public sealed partial class HeadServiceTests
{
    [Fact]
    public async Task Create_project_on_a_connected_remote_creates_it_there_by_its_nickname()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        var result = await Service().HandleAsync(CreateCall((HeadTools.Remote, "Hostinger"), (HeadTools.CreateDirectory, "true")));

        Assert.False(result.IsError, result.Text);
        Assert.Equal("on hostinger: shop created and opened; its root is /srv/shop.", result.Text);
        Assert.Equal((Hostinger, new RemoteProjectRequest("shop", "/srv/shop", true, true)), Assert.Single(_remotes.Creations));
        Assert.Empty(_created);
        Assert.Empty(_remotes.Forwarded);
        Assert.Empty(_approvals.Asked);
    }

    [Fact]
    public async Task Create_project_on_a_remote_passes_open_false_through()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        await Service().HandleAsync(CreateCall((HeadTools.Remote, "hostinger"), (HeadTools.Open, "false")));

        Assert.False(Assert.Single(_remotes.Creations).Request.Open);
    }

    [Fact]
    public async Task Create_project_on_a_remote_needs_no_existing_project_there()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp with { Projects = [] }];

        var result = await Service().HandleAsync(CreateCall((HeadTools.Remote, "hostinger")));

        Assert.False(result.IsError, result.Text);
        Assert.Single(_remotes.Creations);
    }

    [Fact]
    public async Task Create_project_on_a_remote_names_its_error()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];
        _remotes.CreateReply = McpResult.Error("A project named 'shop' already exists.");

        var result = await Service().HandleAsync(CreateCall((HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Equal("on hostinger: A project named 'shop' already exists.", result.Text);
    }

    [Fact]
    public async Task Create_project_on_an_unknown_remote_creates_nothing()
    {
        KnowHostinger();

        var result = await Service().HandleAsync(CreateCall((HeadTools.Remote, "nas")));

        Assert.True(result.IsError);
        Assert.Contains("no remote machine is nicknamed 'nas'", result.Text);
        Assert.Empty(_remotes.Creations);
        Assert.Empty(_created);
    }

    [Fact]
    public async Task Create_project_on_an_ambiguous_nickname_creates_nothing()
    {
        _known.Remotes =
        [
            new KnownRemote(Hostinger, "hostinger", DateTimeOffset.UnixEpoch),
            new KnownRemote("red@other", "hostinger", DateTimeOffset.UnixEpoch),
        ];

        var result = await Service().HandleAsync(CreateCall((HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Contains("more than one remote machine is nicknamed 'hostinger'", result.Text);
        Assert.Empty(_remotes.Creations);
    }

    [Fact]
    public async Task Create_project_on_a_remote_that_cannot_connect_creates_nothing()
    {
        KnowHostinger();
        _remotes.OnConnect = new RemoteMachine(Hostinger, Hostinger, RemoteState.Failed, [], Error: "Connection timed out");

        var result = await Service().HandleAsync(CreateCall((HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Contains("Connection timed out", result.Text);
        Assert.Empty(_remotes.Creations);
    }

    [Fact]
    public async Task Create_project_on_a_remote_checks_its_arguments_before_reaching_it()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        var result = await Service().HandleAsync(
            Call(HeadTools.CreateProject, (HeadTools.Name, "shop"), (HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Contains("'path' is required", result.Text);
        Assert.Empty(_remotes.Creations);
        Assert.Empty(_remotes.Connected);
    }

    [Fact]
    public async Task In_iso_mode_create_project_on_a_remote_is_refused()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];
        _iso.Config = IsoConfig.Off with { On = true };

        var result = await Service().HandleAsync(CreateCall((HeadTools.Remote, "hostinger")));

        Assert.True(result.IsError);
        Assert.Equal(IsoGuard.Refusal(IsoGuard.Ssh), result.Text);
        Assert.Empty(_remotes.Creations);
        Assert.Empty(_remotes.Connected);
    }

    [Fact]
    public async Task Create_project_asked_by_another_machine_creates_it_here_behind_this_machines_rule()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.CreateProject, ActionPolicy.Ask);
        Open(Web, Idle);

        var result = await Service().ServeCreateProjectAsync(new RemoteProjectRequest("shop", "/srv/shop", true, true));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("created and opened", result.Text);
        Assert.Equal([new Fleet.Features.Head.ServeHead.Models.ProjectCreation("shop", "/srv/shop", true)], _created);
        Assert.Equal("create_project", Assert.Single(_approvals.Asked).Tool);
        Assert.Contains("shop", _opened);
    }

    [Fact]
    public async Task Create_project_asked_by_another_machine_can_skip_opening()
    {
        Open(Web, Idle);

        var result = await Service().ServeCreateProjectAsync(new RemoteProjectRequest("shop", "/srv/shop", false, false));

        Assert.Contains("created, not opened", result.Text);
        Assert.DoesNotContain("shop", _opened);
    }

    [Fact]
    public async Task Create_project_asked_by_another_machine_with_no_open_project_is_refused()
    {
        _settings.Config = SettingsConfig.Default.With(HarnessTool.CreateProject, ActionPolicy.Ask);
        var result = await Service().ServeCreateProjectAsync(new RemoteProjectRequest("shop", "/srv/shop", false, true));

        Assert.True(result.IsError);
        Assert.Contains("no project is open", result.Text);
        Assert.Empty(_created);
    }
}
