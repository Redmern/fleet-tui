using Fleet.Features.Todos.OpenTicket;
using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Todos;

namespace Fleet.Tests.Features.Todos;

public sealed class OpenTicketTests : IDisposable
{
    private const string Project = "techweb";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    public OpenTicketTests() => Directory.CreateDirectory(TodoPaths.Folder(_root));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private OpenTicketHandler Handler => new(_mux);

    private string Ticket(string id)
    {
        var file = TodoPaths.File(_root, id);
        File.WriteAllText(file, "---\ntitle: \"x\"\n---\n");
        return file;
    }

    private Task<PaneId> DashboardAsync() =>
        _mux.SpawnAsync(new SpawnOptions { Cwd = _root, SessionName = Project });

    private async Task<Pane> PaneOfAsync(string id) =>
        (await _mux.ListPanesAsync()).Single(p => p.PaneTitle == OpenTicketHandler.PaneTitle(id));

    private int Opened => _mux.Calls.Count(c => c is "spawn" or "split");

    [Fact]
    public async Task A_ticket_opens_its_markdown_file_in_nvim_in_the_project_window()
    {
        var file = Ticket("0001-login");
        var dashboard = await DashboardAsync();

        var result = await Handler.HandleAsync(Project, _root, "0001-login");

        Assert.True(result.Succeeded, result.Error);
        var pane = await PaneOfAsync("0001-login");
        var args = _mux.ArgsFor(pane.Id);
        Assert.Equal(AgentHarness.BrowseCommandFor("todo 0001-login", file), args);
        Assert.Equal(file, args[^1]);
        Assert.True(PathKey.Same(TodoPaths.Folder(_root), pane.Cwd));
        Assert.Equal((await _mux.ListPanesAsync()).Single(p => p.Id == dashboard).WindowId, pane.WindowId);
        Assert.DoesNotContain("split", _mux.Calls);
        Assert.True(pane.IsActive);
    }

    [Fact]
    public async Task The_ticket_pane_launches_nvim_so_it_gets_the_configured_nvim_config()
    {
        var file = Ticket("0001-login");
        var fake = new FakeMuxDriver();
        await fake.SpawnAsync(new SpawnOptions { Cwd = _root, SessionName = Project });

        await new OpenTicketHandler(new NvimConfigDriver(fake, () => true)).HandleAsync(Project, _root, "0001-login");

        var pane = (await fake.ListPanesAsync()).Single(p => p.PaneTitle == OpenTicketHandler.PaneTitle("0001-login"));
        Assert.True(AgentHarness.LaunchesNvim(fake.ArgsFor(pane.Id)));
        Assert.Equal(AgentHarness.FleetNvimAppName, fake.EnvFor(pane.Id)[AgentHarness.NvimAppNameVariable]);
        Assert.Equal(file, fake.ArgsFor(pane.Id)[^1]);
    }

    [Fact]
    public async Task A_ticket_already_open_is_focused_instead_of_opening_another()
    {
        Ticket("0001-login");
        var dashboard = await DashboardAsync();
        await Handler.HandleAsync(Project, _root, "0001-login");
        var first = await PaneOfAsync("0001-login");
        await _mux.FocusPaneAsync(dashboard);
        var before = Opened;

        var result = await Handler.HandleAsync(Project, _root, "0001-login");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(before, Opened);
        Assert.Equal(first.Id, (await PaneOfAsync("0001-login")).Id);
        Assert.True((await PaneOfAsync("0001-login")).IsActive);
    }

    [Fact]
    public async Task A_second_ticket_is_split_beside_an_open_ticket_pane()
    {
        Ticket("0001-login");
        Ticket("0002-logout");
        await DashboardAsync();
        await Handler.HandleAsync(Project, _root, "0001-login");

        var result = await Handler.HandleAsync(Project, _root, "0002-logout");

        Assert.True(result.Succeeded, result.Error);
        Assert.Contains("split", _mux.Calls);
        Assert.Equal((await PaneOfAsync("0001-login")).TabId, (await PaneOfAsync("0002-logout")).TabId);
    }

    [Fact]
    public async Task The_ticket_pane_is_never_taken_for_the_dashboard()
    {
        Ticket("0001-login");
        await DashboardAsync();

        await Handler.HandleAsync(Project, _root, "0001-login");

        Assert.False(PathKey.Same(_root, (await PaneOfAsync("0001-login")).Cwd));
    }

    [Fact]
    public async Task A_ticket_whose_file_is_gone_is_reported_rather_than_opened()
    {
        await DashboardAsync();
        var before = Opened;

        var result = await Handler.HandleAsync(Project, _root, "0009-gone");

        Assert.False(result.Succeeded);
        Assert.Contains("0009-gone", result.Error);
        Assert.Equal(before, Opened);
    }

    [Fact]
    public void Existing_browse_panes_keep_their_command_without_a_file()
    {
        var command = AgentHarness.BrowseCommandFor("files");

        Assert.Equal([.. command.Take(5), .. AgentHarness.BrowseCommand], command);
        Assert.Equal("files", AgentHarness.TitledPaneTitle(command));
    }
}
