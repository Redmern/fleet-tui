using Fleet.Ports.Forge.Models;

namespace Fleet.Ports.Forge;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(
        string executable,
        IReadOnlyList<string> args,
        string workDir,
        CancellationToken ct = default);
}
