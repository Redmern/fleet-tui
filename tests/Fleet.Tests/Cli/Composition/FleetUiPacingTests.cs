using Fleet.Cli.Composition;
using Terminal.Gui.App;

namespace Fleet.Tests.Cli.Composition;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FleetUiPacingCollection
{
    public const string Name = "fleet-ui-pacing";
}

[Collection(FleetUiPacingCollection.Name)]
public sealed class FleetUiPacingTests : IDisposable
{
    private readonly ushort _before = Application.MaximumIterationsPerSecond;

    public void Dispose() => Application.MaximumIterationsPerSecond = _before;

    [Fact]
    public void The_main_loop_runs_at_one_hundred_iterations_a_second_by_default()
    {
        Assert.Equal(100, FleetUi.IterationsPerSecond);
        Assert.Equal(100, FleetUi.Iterations(null));
        Assert.Equal(100, FleetUi.Iterations(string.Empty));
    }

    [Theory]
    [InlineData("25", 25)]
    [InlineData(" 60 ", 60)]
    [InlineData("1000", 1000)]
    public void The_iterations_variable_overrides_the_rate(string value, int expected) =>
        Assert.Equal(expected, FleetUi.Iterations(value));

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("fast")]
    [InlineData("70000")]
    public void An_unusable_override_falls_back_to_the_default(string value) =>
        Assert.Equal(FleetUi.IterationsPerSecond, FleetUi.Iterations(value));

    [Fact]
    public void Pace_sets_the_terminal_gui_iteration_cap()
    {
        Application.MaximumIterationsPerSecond = Application.DefaultMaximumIterationsPerSecond;

        FleetUi.Pace(null);

        Assert.Equal(FleetUi.IterationsPerSecond, Application.MaximumIterationsPerSecond);
        Assert.Equal("FLEET_TUI_ITERATIONS", FleetUi.IterationsVariable);
    }
}
