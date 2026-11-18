namespace Fleet.Ports.Forge.Models;

public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public const int NotFound = 127;

    public bool Ok => ExitCode == 0;

    public bool Missing => ExitCode == NotFound;

    public string Out => StdOut.Trim();

    public string Message => StdErr.Trim().Length > 0 ? StdErr.Trim() : Out.Length > 0 ? Out : $"exit {ExitCode}";
}
