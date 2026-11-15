using Fleet.Ui;

namespace Fleet.Tests;

// FleetModal keeps the open-dialog depth for the whole process. Tests that open a real dialog or assert on that
// depth derive from this class, which gives each test its own depth through FleetModal.Scope, so they run in
// parallel without seeing each other's claims.
public abstract class ModalDepthScope : IDisposable
{
    private readonly IDisposable _scope = FleetModal.Scope();

    public void Dispose()
    {
        _scope.Dispose();
        GC.SuppressFinalize(this);
    }
}

// Serial, for a test that must use the process-wide FleetModal depth rather than a ModalDepthScope: xunit runs this
// collection on its own, after the parallel ones, so no other test claims the depth meanwhile.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ModalDepthCollection
{
    public const string Name = "modal-depth (uses the process-wide FleetModal depth)";
}
