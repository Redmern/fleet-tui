using Fleet.Ports.Remotes.Models;

namespace Fleet.Ports.Remotes;

public interface IKnownRemoteStore
{
    IReadOnlyList<KnownRemote> Load();

    void Remember(string host, DateTimeOffset connected);

    void Rename(string host, string? nickname);

    void Forget(string host);
}
