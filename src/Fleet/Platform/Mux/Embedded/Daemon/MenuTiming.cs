using System.Globalization;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public sealed record MenuOpenRecord(
    string Client, bool Warm, double SettleMs, double FirstFrameMs, string? Host = null, double? LinkMs = null)
{
    private const char Separator = ';';

    public string Line =>
        $"menu timing {Client}: {(Warm ? "warm" : "cold")}, first frame {Ms(FirstFrameMs)} ms, settle {Ms(SettleMs)} ms"
        + (Host is null ? string.Empty : $", link {Ms(LinkMs ?? 0)} ms via {Host}");

    public string Report() =>
        string.Join(Separator, Warm ? "warm" : "cold", Number(SettleMs), Number(FirstFrameMs));

    public static (bool Warm, double SettleMs, double FirstFrameMs)? ReadReport(string? report) =>
        report?.Split(Separator) is [var start and ("warm" or "cold"), var settle, var first]
        && double.TryParse(settle, NumberStyles.Float, CultureInfo.InvariantCulture, out var settleMs)
        && double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var firstMs)
            ? (start == "warm", settleMs, firstMs)
            : null;

    private static string Ms(double ms) => ms.ToString("0", CultureInfo.InvariantCulture);

    private static string Number(double ms) => ms.ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class MenuTiming(TimeProvider clock)
{
    public const string Variable = "FLEET_MENU_TIMING";

    public const string ReportEffect = "menu-timing";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Open> _opens = new(StringComparer.Ordinal);

    public static bool On(string? value) =>
        value is not null && (value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase));

    public void Begin(string client, string? host = null)
    {
        lock (_gate)
        {
            _opens[client] = new Open(clock.GetTimestamp(), host);
        }
    }

    public void Opened(string client, string pane, bool warm)
    {
        lock (_gate)
        {
            if (_opens.TryGetValue(client, out var open) && open.Host is null)
            {
                open.Pane = pane;
                open.Warm = warm;
                open.OpenedAt = clock.GetTimestamp();
            }
        }
    }

    public void Revealed(string pane)
    {
        lock (_gate)
        {
            foreach (var open in _opens.Values.Where(o => o.Pane == pane && o.RevealedAt is null))
            {
                open.RevealedAt = clock.GetTimestamp();
            }
        }
    }

    public void Reported(string host, string report, long outputs)
    {
        if (MenuOpenRecord.ReadReport(report) is not { } remote)
        {
            return;
        }

        lock (_gate)
        {
            foreach (var open in _opens.Values.Where(o => string.Equals(o.Host, host, StringComparison.OrdinalIgnoreCase) && o.Remote is null))
            {
                open.Remote = remote;
                open.OutputsAtReport = outputs;
            }
        }
    }

    public MenuOpenRecord? Framed(string client, Func<string, bool> menuShown, Func<string, long> remoteOutputs)
    {
        lock (_gate)
        {
            if (!_opens.TryGetValue(client, out var open))
            {
                return null;
            }

            var now = clock.GetTimestamp();
            MenuOpenRecord? record = null;

            if (open.Host is { } host && open.Remote is var (warm, settle, remoteFirst) && remoteOutputs(host) > open.OutputsAtReport)
            {
                var first = Ms(open.BegunAt, now);
                record = new MenuOpenRecord(client, warm, settle, first, host, first - remoteFirst);
            }
            else if (open.Host is null && open.Pane is { } pane && open.OpenedAt is { } opened && menuShown(pane))
            {
                var settled = open.RevealedAt is { } revealed ? Ms(opened, revealed) : 0;
                record = new MenuOpenRecord(client, open.Warm, settled, Ms(open.BegunAt, now));
            }

            if (record is not null)
            {
                _opens.Remove(client);
            }

            return record;
        }
    }

    private double Ms(long from, long to) => clock.GetElapsedTime(from, to).TotalMilliseconds;

    private sealed class Open(long begunAt, string? host)
    {
        public long BegunAt { get; } = begunAt;

        public string? Host { get; } = host;

        public string? Pane { get; set; }

        public bool Warm { get; set; }

        public long? OpenedAt { get; set; }

        public long? RevealedAt { get; set; }

        public (bool Warm, double SettleMs, double FirstFrameMs)? Remote { get; set; }

        public long OutputsAtReport { get; set; }
    }
}
