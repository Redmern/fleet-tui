using System.Globalization;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness.Enums;

namespace Fleet.Shared.Harness;

public static class PumpMarkers
{
    public static async Task<PumpOutcome> SeenAsync(
        string folder, TimeSpan within, TimeSpan every, TimeProvider clock, CancellationToken ct = default)
    {
        var dir = Path.Combine(folder, ".fleet");
        var written = new DateTimeOffset(
            File.GetLastWriteTimeUtc(Path.Combine(dir, AgentHarness.AgentInstructionFile))).ToUnixTimeSeconds();
        var seen = Path.Combine(dir, AgentHarness.InstructionSeenFile);
        var unconfirmed = Path.Combine(dir, AgentHarness.InstructionUnconfirmedFile);

        for (var waited = TimeSpan.Zero; ; waited += every)
        {
            if (AtLeast(seen, written))
            {
                return PumpOutcome.Seen;
            }

            if (AtLeast(unconfirmed, written))
            {
                return PumpOutcome.Unconfirmed;
            }

            if (waited >= within || every <= TimeSpan.Zero)
            {
                return PumpOutcome.NotSeen;
            }

            await Task.Delay(every, clock, ct).ConfigureAwait(false);
        }
    }

    private static bool AtLeast(string marker, long written)
    {
        try
        {
            return File.Exists(marker)
                && long.TryParse(File.ReadAllText(marker).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var at)
                && at >= written;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
