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

    private sealed class FakeRemotes(IReadOnlyList<RemoteMachine> machines) : IRemoteMachines
    {
        public Task<IReadOnlyList<RemoteMachine>> ListAsync(CancellationToken ct = default) => Task.FromResult(machines);

        public Task ConnectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task AnswerAsync(string host, string answer, CancellationToken ct = default) => Task.CompletedTask;

        public Task DisconnectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;

        public Task OpenInNewWindowAsync(string host, string project, CancellationToken ct = default) => Task.CompletedTask;

        public Task ShowHereAsync(string host, string project, CancellationToken ct = default) => Task.CompletedTask;

        public Task NewProjectAsync(string host, CancellationToken ct = default) => Task.CompletedTask;
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
