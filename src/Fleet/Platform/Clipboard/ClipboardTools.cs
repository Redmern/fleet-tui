using System.Diagnostics;

namespace Fleet.Platform.Clipboard;

public delegate Task<(int Code, byte[] Stdout)?> ClipboardTool(string program, IReadOnlyList<string> args, CancellationToken ct);

public static class ClipboardTools
{
    public static readonly TimeSpan ReadWithin = TimeSpan.FromSeconds(3);

    public static async Task<(int Code, byte[] Stdout)?> RunAsync(string program, IReadOnlyList<string> args, CancellationToken ct)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var within = CancellationTokenSource.CreateLinkedTokenSource(ct);
        within.CancelAfter(ReadWithin);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            process.StandardInput.Close();
            using var stdout = new MemoryStream();
            var drainErrors = process.StandardError.ReadToEndAsync(within.Token);
            try
            {
                await process.StandardOutput.BaseStream.CopyToAsync(stdout, within.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(within.Token).ConfigureAwait(false);
                await drainErrors.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            return (process.ExitCode, stdout.ToArray());
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
