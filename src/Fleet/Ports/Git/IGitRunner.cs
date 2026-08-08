namespace Fleet.Ports.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;

    public string Out => StdOut.Trim();

    /// <summary>
    /// git's own message is what diagnoses a bad ref or a locked worktree; a bare
    /// exit code is useless for that.
    /// </summary>
    public string Message => StdErr.Trim().Length > 0 ? StdErr.Trim() : $"exit {ExitCode}";
}

public interface IGitRunner
{
    /// <summary>
    /// Runs git in <paramref name="workDir"/>.
    /// </summary>
    /// <param name="stdin">
    /// Optional. Needed by plumbing: `git mktree` reads an empty stdin to produce
    /// the empty-tree hash, which is how a bare repository gets its first commit.
    /// </param>
    Task<GitResult> RunAsync(
        string workDir,
        IReadOnlyList<string> args,
        string? stdin = null,
        CancellationToken ct = default);
}
