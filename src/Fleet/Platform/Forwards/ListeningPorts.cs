using System.Globalization;
using System.Text.RegularExpressions;
using Fleet.Platform.Forwards.Models;

namespace Fleet.Platform.Forwards;

public static partial class ListeningPorts
{
    public const string Command = "ss -ltnHp 2>/dev/null || cat /proc/net/tcp /proc/net/tcp6 2>/dev/null";

    public const int Lowest = 1024;

    private const string ListenState = "0A";

    public static IReadOnlyList<ListeningPort> Parse(string output)
    {
        var ports = new List<ListeningPort>();

        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("LISTEN", StringComparison.Ordinal))
            {
                if (FromSs(line) is { } fromSs)
                {
                    ports.Add(fromSs);
                }
            }
            else if (FromProc(line) is { } fromProc)
            {
                ports.Add(fromProc);
            }
        }

        return ports;
    }

    public static bool Worth(ListeningPort port) =>
        port.Port >= Lowest && !string.Equals(port.Process, "sshd", StringComparison.Ordinal);

    public static IReadOnlyDictionary<int, IReadOnlyList<string>> ByPort(IEnumerable<ListeningPort> ports) =>
        ports.Where(Worth)
            .GroupBy(p => p.Port)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(p => p.Address).Distinct()]);

    public static string Target(IReadOnlyList<string> addresses)
    {
        if (addresses.Count == 0 || addresses.Any(a => a is "127.0.0.1" or "0.0.0.0" or "*" or "::"))
        {
            return "127.0.0.1";
        }

        if (addresses.Contains("::1"))
        {
            return "[::1]";
        }

        var specific = addresses[0];
        return specific.Contains(':', StringComparison.Ordinal) ? $"[{specific}]" : specific;
    }

    private static ListeningPort? FromSs(string line)
    {
        var columns = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length < 4)
        {
            return null;
        }

        var local = columns[3];
        var colon = local.LastIndexOf(':');
        if (colon <= 0 || !int.TryParse(local.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var port))
        {
            return null;
        }

        var address = local[..colon].Trim('[', ']');
        var percent = address.IndexOf('%', StringComparison.Ordinal);
        if (percent >= 0)
        {
            address = address[..percent];
        }

        var process = ProcessName().Match(line) is { Success: true } found ? found.Groups[1].Value : null;
        return new ListeningPort(port, address, process);
    }

    private static ListeningPort? FromProc(string line)
    {
        var columns = line.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (columns.Length < 4 || !columns[0].EndsWith(':') || columns[3] != ListenState)
        {
            return null;
        }

        var local = columns[1].Split(':');
        if (local.Length != 2
            || !int.TryParse(local[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var port)
            || ProcAddress(local[0]) is not { } address)
        {
            return null;
        }

        return new ListeningPort(port, address);
    }

    private static string? ProcAddress(string hex)
    {
        if (hex.Length is not (8 or 32) || !hex.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        var bytes = Convert.FromHexString(hex);
        for (var word = 0; word < bytes.Length; word += 4)
        {
            Array.Reverse(bytes, word, 4);
        }

        return new System.Net.IPAddress(bytes) switch
        {
            var v4 when bytes.Length == 4 => v4.ToString(),
            var any when any.Equals(System.Net.IPAddress.IPv6Any) => "::",
            var v6 => v6.ToString(),
        };
    }

    [GeneratedRegex("users:\\(\\(\"([^\"]+)\"")]
    private static partial Regex ProcessName();
}
