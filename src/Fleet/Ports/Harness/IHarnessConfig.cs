using Fleet.Shared.Results;

namespace Fleet.Ports.Harness;

public interface IHarnessConfig
{
    Result WriteForOrchestration(string folder, string project, string caller);

    Result WriteForAgent(string folder, string project, string repository, string branch);
}
