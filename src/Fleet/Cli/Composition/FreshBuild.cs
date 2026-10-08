using System.Diagnostics;

namespace Fleet.Cli.Composition;

public sealed class FreshBuild(string executable)
{
    private readonly DateTime _startedWith = Stamp(executable);

    public bool Replaced => Stamp(executable) > _startedWith;

    public int RunAgain(IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var next = Process.Start(start);

        if (next is null)
        {
            return 1;
        }

        next.WaitForExit();
        return next.ExitCode;
    }

    private static DateTime Stamp(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }
}
