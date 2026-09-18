using System.Diagnostics;
using Fleet.Ports.Mux.Exceptions;

namespace Fleet.Platform.Mux.WezTerm;

public sealed class WezTermCli(string executable = "wezterm", string? pinnedSocket = null)
{
    private string? _socket;
    private bool _resolved;

    public const string NoAutoStart = "--no-auto-start";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public static IReadOnlyList<string> Argv(IReadOnlyList<string> args) =>
        ["cli", NoAutoStart, .. args];

    public async Task<string> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var socket = await SocketAsync(ct).ConfigureAwait(false);

        return await RunOnAsync(args, socket, ct).ConfigureAwait(false);
    }

    private async Task<string?> SocketAsync(CancellationToken ct)
    {
        if (pinnedSocket is not null)
        {
            return pinnedSocket;
        }

        if (_resolved)
        {
            return _socket;
        }

        _resolved = true;
        _socket = WezTermSockets.FromEnvironment();

        if (_socket is not null)
        {
            return _socket;
        }

        foreach (var candidate in WezTermSockets.Candidates(WezTermSockets.RuntimeDirectory))
        {
            try
            {
                await RunOnAsync(["list", "--format", "json"], candidate, ct).ConfigureAwait(false);

                _socket = candidate;
                return _socket;
            }
            catch (Exception e) when (e is MuxUnavailableException or TimeoutException)
            {
            }
        }

        return _socket;
    }

    private async Task<string> RunOnAsync(
        IReadOnlyList<string> args, string? socket, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (socket is not null)
        {
            psi.Environment[WezTermSockets.Variable] = socket;
        }

        foreach (var a in Argv(args))
        {
            psi.ArgumentList.Add(a);
        }

        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new MuxUnavailableException("wezterm is not on PATH", e);
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(Timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(deadline.Token);
        var stderr = process.StandardError.ReadToEndAsync(deadline.Token);

        try
        {
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"wezterm cli {string.Join(' ', args)} timed out");
        }

        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error)
                ? $"exit {process.ExitCode}"
                : error.Trim();

            throw new MuxUnavailableException($"wezterm cli {args[0]}: {message}");
        }

        return output;
    }

    private static void TryKill(Process p)
    {
        try
        {
            p.Kill(entireProcessTree: true);
        }
        catch (Exception e)
            when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
    }
}
