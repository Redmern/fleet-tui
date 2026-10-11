using System.Diagnostics;

namespace Fleet.Platform.Clipboard;

public sealed record ClipboardRun(int Code, byte[] Stdout, bool Truncated);

public delegate Task<ClipboardRun?> ClipboardTool(string program, IReadOnlyList<string> args, int limit, CancellationToken ct);

public static class ClipboardTools
{
    public static readonly TimeSpan ReadWithin = TimeSpan.FromSeconds(3);

    public static async Task<ClipboardRun?> RunAsync(string program, IReadOnlyList<string> args, int limit, CancellationToken ct)
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
            var drainErrors = process.StandardError.ReadToEndAsync(within.Token);
            try
            {
                var stdout = await ReadUpToAsync(process.StandardOutput.BaseStream, limit, within.Token).ConfigureAwait(false);
                if (stdout.Length >= limit)
                {
                    process.Kill(entireProcessTree: true);
                    return new ClipboardRun(0, stdout, Truncated: true);
                }

                await process.WaitForExitAsync(within.Token).ConfigureAwait(false);
                await drainErrors.ConfigureAwait(false);
                return new ClipboardRun(process.ExitCode, stdout, Truncated: false);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static async Task<byte[]> ReadUpToAsync(Stream stream, int limit, CancellationToken ct)
    {
        using var kept = new MemoryStream();
        var buffer = new byte[81920];
        while (kept.Length < limit)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, limit - kept.Length)), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            kept.Write(buffer, 0, read);
        }

        return kept.ToArray();
    }
}
