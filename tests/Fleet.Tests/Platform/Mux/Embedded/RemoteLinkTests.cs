using Fleet.Features.Projects.SwitchProject;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;
using Fleet.Ui.Models;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class RemoteLinkTests : IAsyncLifetime
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _running = [];
    private Endpoint _home = null!;
    private Endpoint _far = null!;
    private Func<string, string, RemoteChannel>? _open;

    private static Endpoint NewEndpoint()
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        return new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
    }

    private FleetDaemon Start(Endpoint endpoint, FakePanes panes, Func<string, string, RemoteChannel>? open = null)
    {
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = endpoint,
            Pty = panes.NewPty,
            Terminal = panes.NewTerminal,
            FleetExecutable = "fleet",
            RemoteOpen = open,
        });
        _running.Add(daemon.RunAsync(_stop.Token));
        return daemon;
    }

    public Task InitializeAsync()
    {
        _home = NewEndpoint();
        _far = NewEndpoint();
        Start(_far, new FakePanes());
        Start(_home, new FakePanes(), (host, token) => _open!(host, token));
        _open = (_, _) =>
        {
            var stream = _far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return new RemoteChannel(stream, null, stream);
        };
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        await Task.WhenAll(_running);
    }

    private async Task<IReadOnlyList<RemoteDto>> RemotesAsync()
    {
        using var home = new EmbeddedDriver(_home);
        return await home.RemotesAsync();
    }

    private static async Task Eventually(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.True(await condition());
    }

    [Fact]
    public async Task A_connected_remote_lists_its_projects_by_its_machine_name_until_it_is_disconnected()
    {
        await using (var far = await DaemonTests.TestClient.ConnectAsync(_far, ClientRoles.Control, 0, 0, null))
        {
            foreach (var project in (string[])["homelab", "scraper", "homelab~hidden"])
            {
                Assert.True((await far.RequestAsync(new ControlRequest { Op = "spawn", Workspace = project, Cwd = ".", Args = ["claude"] })).Ok);
            }
        }

        using var home = new EmbeddedDriver(_home);
        await home.ConnectRemoteAsync("red@far");

        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Connected, Projects.Count: 2 });
        var remote = Assert.Single(await RemotesAsync());
        Assert.Equal(("red@far", Environment.MachineName), (remote.Host, remote.Name));
        Assert.Equal(["homelab", "scraper"], remote.Projects);

        await home.DisconnectRemoteAsync("red@far");
        Assert.Empty(await RemotesAsync());
    }

    [Fact]
    public async Task A_remote_that_cannot_be_reached_says_why_and_can_be_retried()
    {
        _open = (_, _) => throw new IOException("ssh: connect to host far port 22: Connection refused");
        using var home = new EmbeddedDriver(_home);

        await home.ConnectRemoteAsync("red@far");
        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Failed });
        Assert.Contains("Connection refused", Assert.Single(await RemotesAsync()).Error, StringComparison.Ordinal);

        _open = (_, _) =>
        {
            var stream = _far.ConnectAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            return new RemoteChannel(stream, null, stream);
        };
        await home.ConnectRemoteAsync("red@far");
        await Eventually(async () => (await RemotesAsync()).SingleOrDefault() is { State: RemoteLink.Connected });
    }

    [Fact]
    public async Task Ssh_asks_through_fleetd_and_gets_the_answer_the_user_typed_once()
    {
        string? token = null;
        _open = (_, t) =>
        {
            token = t;
            var silent = new SilentStream();
            return new RemoteChannel(silent, null, silent);
        };
        using var home = new EmbeddedDriver(_home);
        await home.ConnectRemoteAsync("red@far");
        await Eventually(() => Task.FromResult(token is not null));

        Assert.Equal((true, (string?)null), await home.AskPassAsync(token!, "red@far's password: "));
        var asking = Assert.Single(await RemotesAsync());
        Assert.Equal((RemoteLink.Asking, "red@far's password: ", true), (asking.State, asking.Prompt, asking.Secret));

        await home.AnswerRemoteAsync("red@far", "hunter2");

        Assert.Equal((false, "hunter2"), await home.AskPassAsync(token!, "red@far's password: "));
        Assert.Equal((true, (string?)null), await home.AskPassAsync(token!, "red@far's password: "));
        await Assert.ThrowsAsync<Fleet.Ports.Mux.Exceptions.MuxUnavailableException>(() => home.AskPassAsync("someone-else", "password: "));
    }

    [Fact]
    public void A_host_key_question_is_not_a_secret()
    {
        Assert.False(RemoteLink.IsSecret("Are you sure you want to continue connecting (yes/no/[fingerprint])?"));
        Assert.True(RemoteLink.IsSecret("red@far's password: "));
    }

    [Fact]
    public void The_switcher_gets_an_all_tab_this_machine_and_one_tab_per_remote_named_by_its_machine()
    {
        var tabs = SwitchTabs.For(
            [new PickerEntry("fleet", "this window"), new PickerEntry("pc")],
            [new RemoteMachine("user@homelab", "homelab", RemoteState.Connected, ["api", "scraper"])]);

        Assert.Equal(["All", "this machine", "homelab"], tabs.Tabs.Select(t => t.Title));
        Assert.Equal(["fleet", "pc", "api", "scraper"], tabs.Tabs[0].Entries.Select(e => e.Label));
        Assert.Equal("homelab", tabs.Tabs[0].Entries[2].Detail);
        Assert.Equal(new SwitchTarget("scraper", "user@homelab"), tabs.Targets[2][1]);
        Assert.Equal(new SwitchTarget("pc"), tabs.Targets[SwitchTabs.ThisMachine][1]);
    }

    private sealed class SilentStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => 0;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin) => 0;

        public override void SetLength(long value)
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }
}