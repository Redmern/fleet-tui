using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Models;

namespace Fleet.Platform.Storage;

public sealed class JsonKnownRemoteStore : IKnownRemoteStore
{
    public IReadOnlyList<KnownRemote> Load() =>
        [.. Read()
            .Where(e => !string.IsNullOrWhiteSpace(e.Host))
            .DistinctBy(e => e.Host, StringComparer.OrdinalIgnoreCase)
            .Select(e => new KnownRemote(e.Host, string.IsNullOrWhiteSpace(e.Nickname) ? null : e.Nickname, e.LastConnected))];

    public void Remember(string host, DateTimeOffset connected) =>
        Change(remotes =>
        {
            var index = remotes.FindIndex(r => Same(r.Host, host));
            if (index < 0)
            {
                remotes.Add(new KnownRemote(host, null, connected));
            }
            else
            {
                remotes[index] = remotes[index] with { LastConnected = connected };
            }
        });

    public void Rename(string host, string? nickname) =>
        Change(remotes =>
        {
            var index = remotes.FindIndex(r => Same(r.Host, host));
            if (index >= 0)
            {
                remotes[index] = remotes[index] with { Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim() };
            }
        });

    public void Forget(string host) => Change(remotes => remotes.RemoveAll(r => Same(r.Host, host)));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private void Change(Action<List<KnownRemote>> change)
    {
        var remotes = Load().ToList();
        change(remotes);
        Directory.CreateDirectory(FleetPaths.Config);

        var file = new KnownRemotesFile
        {
            Remotes = [.. remotes.Select(r => new KnownRemoteEntry { Host = r.Host, Nickname = r.Nickname, LastConnected = r.LastConnected })],
        };

        File.WriteAllText(
            FleetPaths.KnownRemotesFile,
            JsonSerializer.Serialize(file, FleetJsonContext.Default.KnownRemotesFile));
    }

    private static List<KnownRemoteEntry> Read()
    {
        try
        {
            return JsonSerializer.Deserialize(
                File.ReadAllText(FleetPaths.KnownRemotesFile),
                FleetJsonContext.Default.KnownRemotesFile)?.Remotes ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
