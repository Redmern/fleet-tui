using Fleet.Platform.Mux.Embedded.Daemon;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class KeyLatencyTests
{
    private readonly SimulatedLinkClock _clock = new();

    private void Wait(int ms) => _clock.Advance(TimeSpan.FromMilliseconds(ms));

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void Timing_is_off_unless_the_switch_says_on(string? value, bool on) =>
        Assert.Equal(on, KeyLatency.On(value));

    [Fact]
    public void A_key_is_timed_to_the_pane_answering_and_to_the_frame_after_it()
    {
        var timing = new KeyLatency(_clock);

        timing.Key("c1", "p7");
        Wait(30);
        timing.Output("p7");
        Wait(12);

        var records = timing.Framed("c1");

        Assert.Equal([new KeyFrameRecord("c1", "p7", OutputMs: 30, FrameMs: 42)], records);
    }

    [Fact]
    public void A_frame_before_the_pane_answers_does_not_time_the_key()
    {
        var timing = new KeyLatency(_clock);

        timing.Key("c1", "p7");
        Wait(5);
        Assert.Empty(timing.Framed("c1"));

        Wait(20);
        timing.Output("p7");
        Wait(4);

        Assert.Equal([new KeyFrameRecord("c1", "p7", 25, 29)], timing.Framed("c1"));
        Assert.Empty(timing.Framed("c1"));
    }

    [Fact]
    public void Output_from_another_pane_does_not_answer_the_key()
    {
        var timing = new KeyLatency(_clock);

        timing.Key("c1", "p7");
        Wait(10);
        timing.Output("p2");

        Assert.Empty(timing.Framed("c1"));
    }

    [Fact]
    public void Keys_sent_in_a_burst_are_each_timed_from_their_own_press()
    {
        var timing = new KeyLatency(_clock);

        timing.Key("c1", "p7");
        Wait(30);
        timing.Key("c1", "p7");
        Wait(50);
        timing.Output("p7");
        Wait(10);
        timing.Key("c1", "p7");

        Assert.Equal(
            [new KeyFrameRecord("c1", "p7", 80, 90), new KeyFrameRecord("c1", "p7", 50, 60)],
            timing.Framed("c1"));
    }

    [Fact]
    public void A_frame_for_one_client_leaves_another_clients_keys_alone()
    {
        var timing = new KeyLatency(_clock);

        timing.Key("c1", "p7");
        timing.Key("c2", "p7");
        Wait(10);
        timing.Output("p7");

        Assert.Single(timing.Framed("c1"));
        Assert.Single(timing.Framed("c2"));
    }

    [Fact]
    public void Keys_that_never_get_an_answer_are_capped()
    {
        var timing = new KeyLatency(_clock);

        for (var i = 0; i < KeyLatency.MaxPending + 10; i++)
        {
            timing.Key("c1", "p7");
        }

        timing.Output("p7");

        Assert.Equal(KeyLatency.MaxPending, timing.Framed("c1").Count);
    }

    [Fact]
    public void A_record_reads_as_one_log_line()
    {
        Assert.Equal(
            "key timing c1 p7: output 30.4 ms, frame 42 ms",
            new KeyFrameRecord("c1", "p7", 30.42, 42.0).Line);
    }
}
