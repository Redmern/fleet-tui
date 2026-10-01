using Fleet.Ports.Remotes.Models;

namespace Fleet.Features.Remotes.ManageRemotes.Models;

public sealed record RemoteEntry(string Host, RemoteMachine? Live, KnownRemote? Known)
{
    public string Label => Known?.Nickname ?? Live?.Label ?? Host;

    public string Described => string.Equals(Label, Host, StringComparison.OrdinalIgnoreCase) ? Host : $"{Label} ({Host})";

    public static IReadOnlyList<RemoteEntry> Merge(IReadOnlyList<RemoteMachine> live, IReadOnlyList<KnownRemote> known)
    {
        var byHost = known
            .DistinctBy(k => k.Host, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(k => k.Host, StringComparer.OrdinalIgnoreCase);
        var connected = live.Select(m => m.Host).ToHashSet(StringComparer.OrdinalIgnoreCase);

        return [
            .. live.Select(m => new RemoteEntry(m.Host, m, byHost.GetValueOrDefault(m.Host))),
            .. byHost.Values
                .Where(k => !connected.Contains(k.Host))
                .OrderByDescending(k => k.LastConnected)
                .Select(k => new RemoteEntry(k.Host, null, k)),
        ];
    }
}
