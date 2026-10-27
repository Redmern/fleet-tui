namespace Fleet.Tests;

// FleetModal keeps the open-dialog depth in static state. Tests that open a
// real dialog or assert on that depth run on their own so they do not see
// each other's claims.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModalDepthCollection
{
    public const string Name = "modal-depth";
}
