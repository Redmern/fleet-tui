using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class PaneEnvTests : IAsyncLifetime
{
    private readonly FakePanes _panes = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<string> _asked = [];
    private Endpoint _endpoint = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        _endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        _running = new FleetDaemon(new DaemonOptions
        {
            Endpoint = _endpoint,
            Pty = _panes.NewPty,
            Terminal = _panes.NewTerminal,
            PaneEnv = cwd =>
            {
                lock (_asked)
                {
                    _asked.Add(cwd);
                }

                return new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = $"profile-for-{Path.GetFileName(cwd)}", ["ACCOUNT_PROFILE"] = string.Empty };
            },
        }).RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        await _running;
    }

    [Fact]
    public async Task Each_pane_gets_the_profile_of_its_folder_and_an_explicit_request_wins()
    {
        await using var control = await DaemonTests.TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null);
        var folder = Path.Combine(Path.GetTempPath(), "rib-project");

        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = "rib",
            NewWindow = true,
            Cwd = folder,
            Args = ["claude"],
        })).Ok);
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = "rib",
            NewWindow = true,
            Cwd = folder,
            Args = ["pinned"],
            Env = new Dictionary<string, string> { ["CLAUDE_CONFIG_DIR"] = "asked-for" },
        })).Ok);

        Assert.Equal("profile-for-rib-project", _panes.ByProgram("claude")!.Env["CLAUDE_CONFIG_DIR"]);
        Assert.Equal(string.Empty, _panes.ByProgram("claude")!.Env["ACCOUNT_PROFILE"]);
        Assert.Equal("asked-for", _panes.ByProgram("pinned")!.Env["CLAUDE_CONFIG_DIR"]);
        Assert.Contains(folder, _asked);
    }

    [Fact]
    public async Task Only_the_dashboard_is_told_it_sits_inside_the_frame()
    {
        await using var control = await DaemonTests.TestClient.ConnectAsync(_endpoint, ClientRoles.Control, 0, 0, null);
        var spawned = await control.RequestAsync(new ControlRequest
        {
            Op = "spawn",
            Session = "alpha",
            NewWindow = true,
            Cwd = ".",
            Args = ["nvim"],
        });
        Assert.True((await control.RequestAsync(new ControlRequest
        {
            Op = "split",
            Pane = spawned.Pane,
            Direction = "right",
            Cwd = ".",
            Args = ["C:/tools/fleet.exe", "dash", "--project", "alpha"],
        })).Ok);

        Assert.Equal("1", _panes.ByProgram("C:/tools/fleet.exe")!.Env[Fleet.Shared.Constants.FramedPane.Variable]);
        Assert.Equal(string.Empty, _panes.ByProgram("nvim")!.Env[Fleet.Shared.Constants.FramedPane.Variable]);
    }
}
