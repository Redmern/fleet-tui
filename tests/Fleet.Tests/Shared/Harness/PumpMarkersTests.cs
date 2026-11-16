using System.Globalization;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Harness.Enums;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.Shared.Harness;

public sealed class PumpMarkersTests : IDisposable
{
    private static readonly TimeSpan Every = TimeSpan.FromMilliseconds(300);

    private readonly string _root = Directory.CreateTempSubdirectory("fleet-markers-").FullName;

    private readonly SkippingClock _clock = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Marker(string name) => Path.Combine(_root, ".fleet", name);

    private long Written()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".fleet"));
        File.WriteAllText(Marker(AgentHarness.AgentInstructionFile), "do it");

        return new DateTimeOffset(File.GetLastWriteTimeUtc(Marker(AgentHarness.AgentInstructionFile))).ToUnixTimeSeconds();
    }

    private static string Text(long at) => at.ToString(CultureInfo.InvariantCulture);

    [Fact]
    public async Task An_instruction_is_seen_once_the_seen_marker_has_reached_its_file_time()
    {
        var written = Written();
        File.WriteAllText(Marker(AgentHarness.InstructionSeenFile), Text(written - 5));
        _clock.Skipped += () => File.WriteAllText(Marker(AgentHarness.InstructionSeenFile), Text(written));

        Assert.Equal(PumpOutcome.Seen, await PumpMarkers.SeenAsync(_root, TimeSpan.FromSeconds(60), Every, _clock));
    }

    [Fact]
    public async Task An_instruction_the_pump_never_marked_is_not_seen_within_the_wait()
    {
        Written();
        File.WriteAllText(Marker(AgentHarness.InstructionSeenFile), "0");

        Assert.Equal(PumpOutcome.NotSeen, await PumpMarkers.SeenAsync(_root, TimeSpan.FromSeconds(60), Every, _clock));
        Assert.Equal(200, _clock.Delays.Count);
    }

    [Fact]
    public async Task An_instruction_the_pump_gave_up_on_is_not_waited_for()
    {
        var written = Written();
        File.WriteAllText(Marker(AgentHarness.InstructionSeenFile), "0");
        File.WriteAllText(Marker(AgentHarness.InstructionUnconfirmedFile), Text(written));

        Assert.Equal(PumpOutcome.Unconfirmed, await PumpMarkers.SeenAsync(_root, TimeSpan.FromSeconds(60), Every, _clock));
        Assert.Empty(_clock.Delays);
    }

    [Fact]
    public async Task An_unconfirmed_marker_of_an_older_instruction_does_not_stop_the_wait()
    {
        var written = Written();
        File.WriteAllText(Marker(AgentHarness.InstructionSeenFile), "0");
        File.WriteAllText(Marker(AgentHarness.InstructionUnconfirmedFile), Text(written - 30));

        Assert.Equal(PumpOutcome.NotSeen, await PumpMarkers.SeenAsync(_root, TimeSpan.FromSeconds(3), Every, _clock));
        Assert.Equal(10, _clock.Delays.Count);
    }

    [Fact]
    public async Task No_poll_interval_looks_once()
    {
        Written();

        Assert.Equal(PumpOutcome.NotSeen, await PumpMarkers.SeenAsync(_root, TimeSpan.FromSeconds(60), TimeSpan.Zero, _clock));
        Assert.Empty(_clock.Delays);
    }
}
