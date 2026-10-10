using Fleet.Platform.Mux.Embedded.Daemon;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class MenuTimingTests
{
    private readonly SimulatedLinkClock _clock = new();

    private static bool Shown(string pane) => true;

    private static bool Unshown(string pane) => false;

    private static long NoOutputs(string host) => 0;

    private void Wait(int ms) => _clock.Advance(TimeSpan.FromMilliseconds(ms));

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Timing_is_off_unless_the_switch_says_on(string? value, bool on) =>
        Assert.Equal(on, MenuTiming.On(value));

    [Fact]
    public void A_warm_menu_that_is_already_drawn_settles_in_no_time()
    {
        var timing = new MenuTiming(_clock);

        timing.Begin("c1");
        Wait(5);
        timing.Opened("c1", "p7", warm: true);
        Wait(7);

        var record = timing.Framed("c1", Shown, NoOutputs);

        Assert.Equal(new MenuOpenRecord("c1", Warm: true, SettleMs: 0, FirstFrameMs: 12), record);
    }

    [Fact]
    public void A_cold_menu_settles_from_its_open_until_it_is_revealed()
    {
        var timing = new MenuTiming(_clock);

        timing.Begin("c1");
        Wait(3);
        timing.Opened("c1", "p7", warm: false);
        Wait(200);
        timing.Revealed("p7");
        Wait(10);

        var record = timing.Framed("c1", Shown, NoOutputs);

        Assert.Equal(new MenuOpenRecord("c1", Warm: false, SettleMs: 200, FirstFrameMs: 213), record);
    }

    [Fact]
    public void An_open_is_recorded_once_and_only_after_a_frame_shows_the_menu()
    {
        var timing = new MenuTiming(_clock);
        Assert.Null(timing.Framed("c1", Shown, NoOutputs));

        timing.Begin("c1");
        Assert.Null(timing.Framed("c1", Shown, NoOutputs));

        timing.Opened("c1", "p7", warm: false);
        Assert.Null(timing.Framed("c1", Unshown, NoOutputs));

        Assert.NotNull(timing.Framed("c1", Shown, NoOutputs));
        Assert.Null(timing.Framed("c1", Shown, NoOutputs));
    }

    [Fact]
    public void A_linked_open_takes_warm_and_settle_from_the_remote_and_counts_the_rest_as_link()
    {
        var timing = new MenuTiming(_clock);
        var far = new MenuOpenRecord("c3", Warm: false, SettleMs: 120, FirstFrameMs: 150);
        long outputs = 4;

        timing.Begin("c1", "red@far");
        Wait(160);
        timing.Reported("red@far", far.Report(), outputs);
        Assert.Null(timing.Framed("c1", Shown, _ => outputs));

        Wait(40);
        outputs = 5;
        var record = timing.Framed("c1", Unshown, _ => outputs);

        Assert.Equal(new MenuOpenRecord("c1", Warm: false, SettleMs: 120, FirstFrameMs: 200, Host: "red@far", LinkMs: 50), record);
    }

    [Fact]
    public void A_report_that_cannot_be_read_is_ignored()
    {
        var timing = new MenuTiming(_clock);

        timing.Begin("c1", "red@far");
        timing.Reported("red@far", "garbage", 0);

        Assert.Null(timing.Framed("c1", Shown, _ => 1));
    }

    [Fact]
    public void A_record_reads_as_one_log_line()
    {
        Assert.Equal(
            "menu timing c1: warm, first frame 12 ms, settle 0 ms",
            new MenuOpenRecord("c1", true, 0, 12.4).Line);
        Assert.Equal(
            "menu timing c1: cold, first frame 200 ms, settle 120 ms, link 50 ms via red@far",
            new MenuOpenRecord("c1", false, 120, 200, "red@far", 50).Line);
    }
}
