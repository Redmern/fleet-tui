using System.Diagnostics;

namespace Fleet.Cli.Composition;

public sealed class FreshBuild(string executable)
{
    public static readonly TimeSpan SettleFor = TimeSpan.FromSeconds(2);

    public static FreshBuild ThisProcess { get; } = new(Adapters.Executable);

    private readonly DateTime _startedWith = Stamp(executable);

    public bool Replaced => Stamp(executable) > _startedWith;

    public bool ReplacedAndSettled =>
        Stamp(executable) is var stamp && stamp > _startedWith && DateTime.UtcNow - stamp >= SettleFor;

    public int RunAgain(IReadOnlyList<string> args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false };

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var next = Process.Start(start);

                if (next is null)
                {
                    return 1;
                }

                next.WaitForExit();
                return next.ExitCode;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException && attempt < 5)
            {
                Thread.Sleep(SettleFor);
            }
        }
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
