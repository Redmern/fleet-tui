using System.Diagnostics;

namespace Fleet.Platform.Clipboard;

public delegate Task<int?> ClipboardFeed(string program, IReadOnlyList<string> args, byte[] input, CancellationToken ct);

public static class ClipboardTextFeed
{
    public static Task<int?> FeedAsync(string program, IReadOnlyList<string> args, byte[] input, CancellationToken ct) =>
        FeedAsync(program, args, input, ClipboardTools.ReadWithin, ct);

    public static async Task<int?> FeedAsync(string program, IReadOnlyList<string> args, byte[] input, TimeSpan within, CancellationToken ct)
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

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(within);

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            Discard(process.StandardOutput.BaseStream);
            Discard(process.StandardError.BaseStream);
            try
            {
                var stdin = process.StandardInput.BaseStream;
                await stdin.WriteAsync(input, deadline.Token).ConfigureAwait(false);
                await stdin.FlushAsync(deadline.Token).ConfigureAwait(false);
                process.StandardInput.Close();
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                return process.ExitCode;
            }
            catch (Exception e) when (e is OperationCanceledException or IOException)
            {
                process.Kill(entireProcessTree: true);
                return null;
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    private static void Discard(Stream output) =>
        _ = output.CopyToAsync(Stream.Null).ContinueWith(
            copied => _ = copied.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
}
