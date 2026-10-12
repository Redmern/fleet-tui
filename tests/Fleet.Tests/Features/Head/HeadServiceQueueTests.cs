using Fleet.Features.Head.ServeHead;
using Fleet.Features.Head.ServeHead.Models;
using Fleet.Platform.Mux.Fake;
using Fleet.Platform.Storage;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Head;

public sealed partial class HeadServiceTests : IDisposable
{
    private readonly List<HeadService> _services = [];

    private HeadService Tracked(HeadService service)
    {
        _services.Add(service);
        return service;
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service.Relay.Stop();
        }
    }

    private static readonly HeadTiming ShortQueue = new(
        TimeSpan.FromMilliseconds(5),
        TimeSpan.Zero,
        TimeSpan.Zero,
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromMilliseconds(50))
    {
        SlowPoll = TimeSpan.FromMilliseconds(20),
    };

    private static List<string> Typed(IReadOnlyList<string> sent) => [.. sent.Where(t => t != RelayText.Submit)];

    [Fact]
    public async Task Queued_prompts_wait_while_the_project_is_closed_and_go_in_in_order_once_it_reopens()
    {
        var (main, dash) = Open(Web, Busy);
        var service = Service(timing: ShortQueue);

        await service.HandleAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "first")));
        await service.HandleAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "second")));

        await _mux.KillPaneAsync(main);
        await _mux.KillPaneAsync(dash);
        _open.Remove("web");
        await Task.Delay(300);

        Assert.Equal(2, service.Relay.Pending("web"));

        var (reopened, _) = Open(Web, Idle);
        await service.Relay.Drained("web").WaitAsync(TimeSpan.FromSeconds(5));

        var typed = Typed(_mux.SentTo(reopened));
        Assert.Equal(2, typed.Count);
        Assert.StartsWith(RelayText.IntoNvimTerminal + "first\n\n", typed[0]);
        Assert.StartsWith(RelayText.IntoNvimTerminal + "second\n\n", typed[1]);
        Assert.All(typed, t => Assert.Contains("delivered late", t));
    }

    [Fact]
    public async Task A_queue_survives_fleet_restarting_and_is_delivered_in_order_when_it_resumes()
    {
        var folder = Directory.CreateTempSubdirectory("fleet-relay-queue-").FullName;

        try
        {
            var before = new FakeMuxDriver();
            var old = await before.SpawnAsync(new SpawnOptions { Cwd = Web.Root, SessionName = Web.Name, NewWindow = true });
            before.SetText(old, Busy);
            _open["web"] = (old, PaneId.None);

            var first = Service(queue: new JsonRelayQueueStore(folder), mux: before);
            await first.HandleAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "first")));
            await first.HandleAsync(Call(HeadTools.Relay, (HeadTools.Project, "web"), (HeadTools.Prompt, "second")));
            _open.Remove("web");

            var (main, _) = Open(Web, Idle);
            var restarted = Service(queue: new JsonRelayQueueStore(folder));

            Assert.Equal(2, restarted.Relay.Pending("web"));

            restarted.Relay.Resume();
            await restarted.Relay.Drained("web").WaitAsync(TimeSpan.FromSeconds(5));

            var typed = Typed(_mux.SentTo(main));
            Assert.Equal(2, typed.Count);
            Assert.StartsWith(RelayText.IntoNvimTerminal + "first\n\n", typed[0]);
            Assert.StartsWith(RelayText.IntoNvimTerminal + ", second\n\n", typed[1]);
            Assert.Empty(before.SentTo(old));
            Assert.Equal(0, restarted.Relay.Pending("web"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task List_projects_shows_how_many_prompts_wait()
    {
        Open(Web, Busy);
        var service = Service();

        await service.HandleAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "status?")));
        await service.HandleAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "and now?")));

        var result = await service.HandleAsync(Call(HeadTools.ListProjects));

        Assert.Contains("web  open  C:/p/web  on local  2 prompt(s) queued", result.Text);
        Assert.Contains("api  closed  C:/p/api  on local", result.Text);
        Assert.DoesNotContain("api  closed  C:/p/api  on local  ", result.Text);
    }

    [Fact]
    public async Task List_queued_numbers_the_prompts_in_delivery_order()
    {
        Open(Web, Busy);
        var service = Service();

        await service.HandleAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "first")));
        await service.HandleAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "second")));

        var one = await service.HandleAsync(Call(HeadTools.ListQueued, (HeadTools.Project, "web")));
        var all = await service.HandleAsync(Call(HeadTools.ListQueued));
        var none = await service.HandleAsync(Call(HeadTools.ListQueued, (HeadTools.Project, "api")));

        var lines = one.Text.Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("web  1  queued ", lines[0]);
        Assert.EndsWith("  first", lines[0]);
        Assert.StartsWith("web  2  queued ", lines[1]);
        Assert.EndsWith("  second", lines[1]);
        Assert.Equal(one.Text, all.Text);
        Assert.Equal("no prompts are queued for api.", none.Text);
    }

    [Fact]
    public async Task Drop_queued_drops_one_by_number_or_all_and_answers_with_their_text()
    {
        var (main, _) = Open(Web, Busy);
        var service = Service();

        foreach (var prompt in new[] { "stale", "keep", "also stale" })
        {
            await service.HandleAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, prompt)));
        }

        var one = await service.HandleAsync(
            Call(HeadTools.DropQueued, (HeadTools.Project, "web"), (HeadTools.Number, "1")));
        var missing = await service.HandleAsync(
            Call(HeadTools.DropQueued, (HeadTools.Project, "web"), (HeadTools.Number, "9")));

        Assert.False(one.IsError, one.Text);
        Assert.Contains("dropped 1 queued prompt(s) for web", one.Text);
        Assert.EndsWith("  stale", one.Text);
        Assert.True(missing.IsError);
        Assert.Equal(["keep", "also stale"], service.Relay.Queued("web").Select(q => q.Text));

        var rest = await service.HandleAsync(Call(HeadTools.DropQueued, (HeadTools.Project, "web")));

        Assert.Contains("dropped 2 queued prompt(s) for web", rest.Text);
        Assert.Contains("  keep", rest.Text);
        Assert.Equal(0, service.Relay.Pending("web"));

        _mux.SetText(main, Idle);
        await service.Relay.Drained("web").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(_mux.SentTo(main));
    }

    [Fact]
    public async Task Served_to_the_origin_the_queue_can_be_listed_and_dropped()
    {
        Open(Web, Busy);
        var service = Service();

        await service.ServeOriginAsync(Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "status?")));

        var listed = await service.ServeOriginAsync(Call(HeadTools.ListQueued));
        var dropped = await service.ServeOriginAsync(Call(HeadTools.DropQueued, (HeadTools.Project, "web")));

        Assert.False(listed.IsError, listed.Text);
        Assert.Contains("web  1  queued ", listed.Text);
        Assert.False(dropped.IsError, dropped.Text);
        Assert.Equal(0, service.Relay.Pending("web"));
    }

    [Fact]
    public async Task List_projects_on_a_remote_shows_how_many_prompts_wait_there()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];
        _remotes.Reply = McpResult.Ok("shop  1  queued 2026-10-12 10:00  hi\nshop  2  queued 2026-10-12 10:05  again");

        var result = await Service().HandleAsync(Call(HeadTools.ListProjects, (HeadTools.Remote, "hostinger")));

        Assert.Equal(
            "DeVrolijkeViervoeters  closed  on hostinger\nshop  open  on hostinger  2 prompt(s) queued", result.Text);
        Assert.Equal(HeadTools.ListQueued, Assert.Single(_remotes.Forwarded).Request.Tool);
    }

    [Fact]
    public async Task Queue_tools_on_a_remote_go_to_that_machines_fleet()
    {
        KnowHostinger();
        _remotes.Machines = [HostingerUp];

        await Service().HandleAsync(Call(HeadTools.ListQueued, (HeadTools.Remote, "hostinger")));
        await Service().HandleAsync(
            Call(HeadTools.DropQueued, (HeadTools.Project, "shop"), (HeadTools.Number, "1"), (HeadTools.Remote, "hostinger")));

        Assert.Equal([HeadTools.ListQueued, HeadTools.DropQueued], _remotes.Forwarded.Select(f => f.Request.Tool));
        Assert.Equal("1", _remotes.Forwarded[1].Request.Value(HeadTools.Number));
    }

    [Fact]
    public async Task Tell_types_into_the_orchestrator_of_an_open_project_whose_pane_started_in_another_root()
    {
        var main = await _mux.SpawnAsync(
            new SpawnOptions { Cwd = "C:/home/red/~/repos/web", SessionName = Web.Name, NewWindow = true });
        await _mux.SetTitleAsync(main, FleetTabTitles.Dashboard);
        var dash = await _mux.SplitAsync(
            new SplitOptions(main, Fleet.Ports.Mux.Enums.SplitDirection.Right) { Cwd = "C:/home/red/~/repos/web" });
        _mux.SetText(main, Idle);
        _mux.SetText(dash, "fleet — web");
        _open["web"] = (main, dash);

        var result = await Service().ServeOriginAsync(
            Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "status?")));

        Assert.False(result.IsError, result.Text);
        Assert.StartsWith("told web's orchestrator", result.Text);
        Assert.Equal([RelayText.IntoNvimTerminal + "status?", RelayText.Submit], _mux.SentTo(main));
        Assert.Empty(_mux.SentTo(dash));
    }

    [Fact]
    public async Task Tell_to_an_open_project_without_an_orchestrator_pane_says_how_to_unblock_it()
    {
        var other = await _mux.SpawnAsync(new SpawnOptions { Cwd = "C:/elsewhere", SessionName = "other", NewWindow = true });
        _mux.SetText(other, Idle);
        _open["web"] = (PaneId.None, PaneId.None);

        var service = Service();
        var result = await service.HandleAsync(
            Call(HeadTools.Tell, (HeadTools.Project, "web"), (HeadTools.Prompt, "status?")));

        Assert.False(result.IsError, result.Text);
        Assert.Contains("queued (1 waiting)", result.Text);

        service.Relay.Stop();
        await service.Relay.Drained("web").WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, service.Relay.Pending("web"));
        Assert.Contains("C:/p/web", result.Text);
        Assert.Contains(HeadTools.SwitchProject, result.Text);
        Assert.Contains(HeadTools.DropQueued, result.Text);
        Assert.Empty(_mux.SentTo(other));
    }
}
