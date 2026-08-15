using Fleet.Ports.Harness;
using Fleet.Shared.Results;

namespace Fleet.Platform.Harness;

public sealed class NullHarnessConfig : IHarnessConfig
{
    public Result WriteForOrchestration(string folder, string project, string caller) => Result.Ok();
}
