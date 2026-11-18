using Fleet.Ports.Forge;
using Fleet.Ports.Forge.Enums;
using Fleet.Ports.Forge.Models;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;

namespace Fleet.Tests.Features.Repositories.OpenPr;

internal sealed class FakeForgeProbe(ForgeProbeResult answer = ForgeProbeResult.NotAForge) : IForgeProbe
{
    public ForgeProbeResult Answer { get; set; } = answer;

    public List<string> Hosts { get; } = [];

    public Task<ForgeProbeResult> ProbeAsync(string host, CancellationToken ct = default)
    {
        Hosts.Add(host);

        return Task.FromResult(Answer);
    }
}

internal sealed class FakeProcessRunner : IProcessRunner
{
    public ProcessResult Answer { get; set; } = new(0, "https://github.com/acme/api/pull/7\n", string.Empty);

    public List<(string Executable, IReadOnlyList<string> Args, string WorkDir)> Runs { get; } = [];

    public bool Installed { get; set; } = true;

    public IEnumerable<IReadOnlyList<string>> Creates => Runs.Select(r => r.Args).Where(a => a[0] == "pr");

    public Task<ProcessResult> RunAsync(
        string executable, IReadOnlyList<string> args, string workDir, CancellationToken ct = default)
    {
        Runs.Add((executable, args, workDir));

        if (!Installed)
        {
            return Task.FromResult(new ProcessResult(ProcessResult.NotFound, string.Empty, "No such file or directory"));
        }

        return Task.FromResult(args[0] == "--version" ? new ProcessResult(0, "gh version 2.60.0\n", string.Empty) : Answer);
    }
}

// Answers git by the first words of the command; anything not scripted succeeds with no output.
internal sealed class ScriptedGit : IGitRunner
{
    public Dictionary<string, GitResult> Answers { get; } = new(StringComparer.Ordinal);

    public List<IReadOnlyList<string>> Runs { get; } = [];

    public IEnumerable<IReadOnlyList<string>> Pushes => Runs.Where(r => r[0] == "push");

    // A command starting with this never answers: it waits until it is cancelled, as a push at a password prompt.
    public string? Hangs { get; set; }

    public async Task<GitResult> RunAsync(
        string workDir, IReadOnlyList<string> args, string? stdin = null, CancellationToken ct = default)
    {
        Runs.Add(args);

        var line = string.Join(' ', args);

        if (Hangs is not null && line.StartsWith(Hangs, StringComparison.Ordinal))
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        return Answer(line);
    }

    private GitResult Answer(string line)
    {
        foreach (var (prefix, answer) in Answers)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                return answer;
            }
        }

        return new GitResult(0, string.Empty, string.Empty);
    }
}
