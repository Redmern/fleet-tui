using Fleet.Platform.Remotes;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Tests.Platform.Remotes;

public sealed class NicknamedRemotesTests
{
    [Fact]
    public async Task List_adds_the_known_nickname_and_keeps_the_fleetd_name()
    {
        var remotes = new NicknamedRemotes(
            new FakeRemotes([new RemoteMachine("user@homelab", "homelab-01", RemoteState.Connected, []), new RemoteMachine("pi@garage", "garage", RemoteState.Connected, [])]),
            new FakeKnown([new KnownRemote("USER@HOMELAB", "lab", DateTimeOffset.UnixEpoch)]));

        var machines = await remotes.ListAsync();

        Assert.Equal(["lab", "garage"], machines.Select(m => m.Label));
        Assert.Equal(["homelab-01", "garage"], machines.Select(m => m.Name));
    }

    [Fact]
    public async Task Create_project_goes_to_the_inner_machines_unchanged()
    {
        var inner = new FakeRemotes([]);
        var remotes = new NicknamedRemotes(inner, new FakeKnown([]));
        var request = new RemoteProjectRequest("shop", "/srv/shop", true, false);

        var result = await remotes.CreateProjectAsync("user@homelab", request);

        Assert.Equal("created", result.Text);
        Assert.Equal(("user@homelab", request), Assert.Single(inner.Creations));
    }

    private sealed class FakeRemotes(IReadOnlyList<RemoteMachine> machines) : IRemoteMachines
    {
        public Task<IReadOnlyList<RemoteMachine>> ListAsync(CancellationToken ct = default) => Task.FromResult(machines);

        public Task ConnectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task AnswerAsync(string host, string answer, CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task OpenInNewWindowAsync(string host, string project, CancellationToken ct = default) => Task.CompletedTask;

        public Task ShowHereAsync(string host, string project, CancellationToken ct = default) => Task.CompletedTask;

        public Task NewProjectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task<Fleet.Ports.Mcp.Models.McpResult> HeadAsync(
            string host, Fleet.Ports.Mcp.Models.McpRequest request, CancellationToken ct = default) =>
            Task.FromResult(Fleet.Ports.Mcp.Models.McpResult.Ok(string.Empty));

        public List<(string Host, RemoteProjectRequest Request)> Creations { get; } = [];

        public Task<Fleet.Ports.Mcp.Models.McpResult> CreateProjectAsync(
            string host, RemoteProjectRequest request, CancellationToken ct = default)
        {
            Creations.Add((host, request));
            return Task.FromResult(Fleet.Ports.Mcp.Models.McpResult.Ok("created"));
        }
    }

    private sealed class FakeKnown(IReadOnlyList<KnownRemote> known) : IKnownRemoteStore
    {
        public IReadOnlyList<KnownRemote> Load() => known;

        public void Remember(string host, DateTimeOffset connected)
        {
        }

        public void Rename(string host, string? nickname)
        {
        }

        public void Forget(string host)
        {
        }
    }
}
