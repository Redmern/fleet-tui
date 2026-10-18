namespace Fleet.Tests;

// Tests that bound real wall-clock latency: daemons and simulated links running on real
// timers, measured with a Stopwatch. Run in parallel with the rest of the suite on a 2-core
// CI runner they lose the thread pool for seconds at a time (a cold menu on a 200 ms link
// once took 3 s to settle and 1.1 s on the link), so they measured the runner, not fleet.
// xunit runs a collection with parallelization off on its own, after the parallel ones.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WallClockCollection
{
    public const string Name = "wall-clock";
}
