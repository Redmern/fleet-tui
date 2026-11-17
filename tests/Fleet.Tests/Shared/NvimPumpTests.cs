using System.Diagnostics;
using System.Globalization;
using Fleet.Shared.Constants;
using Fleet.Tests.Support;

namespace Fleet.Tests.Shared;

internal sealed class NvimPumpRig : IDisposable
{
    public const string Prompt = AgentHarness.AgentInstructionPrompt;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(25);

    private readonly List<string> _roots = [];

    public void Dispose()
    {
        foreach (var root in _roots)
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    public sealed record Run(string Typed, string Written, string Seen, string? Unconfirmed);

    public string NewRoot()
    {
        var root = Directory.CreateTempSubdirectory("fleet-pump-").FullName;

        lock (_roots)
        {
            _roots.Add(root);
        }

        return root;
    }

    public static async Task WriteInstructionAsync(string path, DateTime at)
    {
        await File.WriteAllTextAsync(path, "do it");
        File.SetLastWriteTimeUtc(path, at);
    }

    public static string UnixSeconds(string path) =>
        new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    public static Process Start(string root, string pane, string? secondPane = null)
    {
        var second = secondPane is null
            ? string.Empty
            : "lua vim.schedule(function() vim.cmd('new') vim.fn.jobstart({'sh','-c','"
              + secondPane.Replace("'", "''", StringComparison.Ordinal) + "'},{term=true}) end)";

        var start = new ProcessStartInfo("nvim")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            ArgumentList =
            {
                "--headless", "--clean", "--cmd", "command! -nargs=* Neotree echo",
                "-c", AgentHarness.NvimStartupWithCommand(["sh", "-c", $"{pane}; cat > typed.txt"]),
            },
        };

        if (second.Length > 0)
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(second);
        }

        start.Environment.Remove("TMUX");

        var process = Process.Start(start)!;
        _ = process.StandardOutput.ReadToEndAsync();
        _ = process.StandardError.ReadToEndAsync();

        return process;
    }

    public static async Task UntilAsync(Func<bool> done)
    {
        var until = DateTime.UtcNow + Limit;

        while (!done() && DateTime.UtcNow < until)
        {
            await Task.Delay(100);
        }
    }

    public async Task<Run> RunAsync(string pane)
    {
        var root = NewRoot();
        var fleet = Directory.CreateDirectory(Path.Combine(root, ".fleet")).FullName;
        var instruction = Path.Combine(fleet, AgentHarness.AgentInstructionFile);
        var seen = Path.Combine(fleet, AgentHarness.InstructionSeenFile);
        var unconfirmed = Path.Combine(fleet, AgentHarness.InstructionUnconfirmedFile);
        var typed = Path.Combine(root, "typed.txt");

        await WriteInstructionAsync(instruction, DateTime.UtcNow.AddMinutes(-5));
        await File.WriteAllTextAsync(seen, "0");

        var written = UnixSeconds(instruction);

        using var process = Start(root, pane);

        try
        {
            await UntilAsync(() => File.Exists(unconfirmed) || Read(seen) != "0");
            await Task.Delay(1500);
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }

        return new Run(
            File.Exists(typed) ? await File.ReadAllTextAsync(typed) : string.Empty,
            written,
            Read(seen)!,
            Read(unconfirmed));
    }

    public static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}

public sealed class NvimPumpTests : IDisposable
{
    private readonly NvimPumpRig _rig = new();

    public void Dispose() => _rig.Dispose();

    private const string Prompt = NvimPumpRig.Prompt;

    // What the real pane does with typed text: the line discipline echoes it (a TUI that shows its input), or not (a
    // TUI that dropped it), and the pane's program reads every byte raw so a retype shows as ctrl+u and a second typing.
    private const string Echoing = "stty echo -icanon";

    private const string Dropping = "stty -echo -icanon";

    private static string Earlier(string lead) => $"printf '%s\\n' '{Prompt}'; {lead}";

    // Real nvim runs of about four to five seconds each (the pump's own first tick is at three), taken side by side
    // so they cost one. The echo polls run on nvim's real clock, so a stalled runner gets one more try.
    [NvimFact]
    public async Task The_pump_checks_the_terminal_for_the_prompt_before_it_submits_or_marks_the_file_seen()
    {
        await Timing.RetryOnceAsync(async () =>
        {
            var shown = _rig.RunAsync(Echoing);
            var dropped = _rig.RunAsync(Dropping);
            var shownAgain = _rig.RunAsync(Earlier(Echoing));
            var droppedAgain = _rig.RunAsync(Earlier(Dropping));

            var echoed = await shown;

            Assert.Equal(Prompt + "\n", echoed.Typed);
            Assert.Equal(echoed.Written, echoed.Seen);
            Assert.Null(echoed.Unconfirmed);

            var lost = await dropped;

            Assert.Equal(Prompt + "\u0015" + Prompt + "\u0015", lost.Typed);
            Assert.Equal("0", lost.Seen);
            Assert.Equal(lost.Written, lost.Unconfirmed);

            var echoedBesideEarlier = await shownAgain;

            Assert.Equal(Prompt + "\n", echoedBesideEarlier.Typed);
            Assert.Equal(echoedBesideEarlier.Written, echoedBesideEarlier.Seen);

            var lostBesideEarlier = await droppedAgain;

            Assert.Equal(Prompt + "\u0015" + Prompt + "\u0015", lostBesideEarlier.Typed);
            Assert.Equal("0", lostBesideEarlier.Seen);
            Assert.Equal(lostBesideEarlier.Written, lostBesideEarlier.Unconfirmed);
        });
    }

}

public sealed class NvimPumpDeadJobTests : IDisposable
{
    private const string Prompt = NvimPumpRig.Prompt;

    private readonly NvimPumpRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [NvimFact]
    public async Task A_terminal_whose_job_exits_mid_delivery_does_not_stop_the_pump_for_the_next_instruction()
    {
        await Timing.RetryOnceAsync(async () =>
        {
            var root = _rig.NewRoot();
            var fleet = Directory.CreateDirectory(Path.Combine(root, ".fleet")).FullName;
            var instruction = Path.Combine(fleet, AgentHarness.AgentInstructionFile);
            var seen = Path.Combine(fleet, AgentHarness.InstructionSeenFile);
            var unconfirmed = Path.Combine(fleet, AgentHarness.InstructionUnconfirmedFile);

            await NvimPumpRig.WriteInstructionAsync(instruction, DateTime.UtcNow.AddMinutes(-5));
            await File.WriteAllTextAsync(seen, "0");

            using var process = NvimPumpRig.Start(
                root, "stty -echo -icanon; head -c 5 > /dev/null; exit 0",
                "stty echo -icanon; cat > typed2.txt");

            try
            {
                await NvimPumpRig.UntilAsync(() => File.Exists(unconfirmed));
                await NvimPumpRig.WriteInstructionAsync(instruction, DateTime.UtcNow.AddMinutes(-4));
                var second = NvimPumpRig.UnixSeconds(instruction);
                await NvimPumpRig.UntilAsync(() => NvimPumpRig.Read(seen) == second);
                await Task.Delay(1000);

                Assert.Equal(Prompt + "\n", await File.ReadAllTextAsync(Path.Combine(root, "typed2.txt")));
            }
            finally
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        });
    }
}

internal sealed class NvimFactAttribute : FactAttribute
{
    public NvimFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || !OnPath("nvim"))
        {
            Skip = "needs Linux with nvim on PATH";
        }
    }

    private static bool OnPath(string tool) =>
        (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, tool)));
}
