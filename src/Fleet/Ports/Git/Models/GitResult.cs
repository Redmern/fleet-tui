namespace Fleet.Ports.Git.Models;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;

    public string Out => StdOut.Trim();

    public string Message => StdErr.Trim().Length > 0 ? StdErr.Trim() : $"exit {ExitCode}";
}
