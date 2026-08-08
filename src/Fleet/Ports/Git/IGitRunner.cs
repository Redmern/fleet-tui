using Fleet.Ports.Git.Models;

namespace Fleet.Ports.Git;

public interface IGitRunner
{
    Task<GitResult> RunAsync(
        string workDir,
        IReadOnlyList<string> args,
        string? stdin = null,
        CancellationToken ct = default);
}
