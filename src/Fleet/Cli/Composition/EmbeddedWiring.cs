using System.Diagnostics;
using Fleet.Platform.Mux.Embedded;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Pty;
using Fleet.Ports;
using Fleet.Ports.Mux;

namespace Fleet.Cli.Composition;

public static class EmbeddedWiring
{
    public const string PrefixVariable = "FLEET_PREFIX";

    public const string RemoteCommandVariable = "FLEET_REMOTE_COMMAND";

    private static readonly TimeSpan IdleExit = TimeSpan.FromSeconds(10);

    public static bool Ready => GhosttyNative.Available();

    public static bool InsideClient =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(FleetDaemon.ClientVariable));

    public static IMuxDriver Driver() => new EmbeddedDriver(Endpoint.Default(), StartDaemonAsync);

    public static async Task<int> RunDaemonAsync(IFleetLog log)
    {
        if (!Ready)
        {
            await Console.Error.WriteLineAsync(
                "fleet: this build carries no libghostty-vt, so it cannot run the embedded multiplexer")
                .ConfigureAwait(false);
            return 1;
        }

        if (OperatingSystem.IsWindows() && WindowsConsole.ReleaseRedirectedStdHandles() > 0)
        {
            log.Write("fleetd: released redirected std handles so pane children use their pseudoconsole");
        }

        if (OperatingSystem.IsWindows())
        {
            log.Write($"fleetd: panes use the {ConPtyApi.Current.Name} ConPTY");
        }

        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = Endpoint.Default(),
            Pty = IPanePty.Create,
            Terminal = GhosttyTerminal.Create,
            Log = line => log.Write($"fleetd: {line}"),
            FleetExecutable = Adapters.Executable,
            ExitWhenEmptyAfter = IdleExit,
        });

        await daemon.RunAsync().ConfigureAwait(false);
        return 0;
    }

    public static async Task<int> AttachAsync(string? workspace, string? sshHost, IFleetLog log)
    {
        Stream stream;

        if (sshHost is not null)
        {
            stream = Ssh(sshHost);
        }
        else
        {
            var endpoint = Endpoint.Default();
            var local = await TryConnectAsync(endpoint).ConfigureAwait(false);

            if (local is null && await StartDaemonAsync().ConfigureAwait(false))
            {
                for (var i = 0; i < 50 && local is null; i++)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    local = await TryConnectAsync(endpoint).ConfigureAwait(false);
                }
            }

            if (local is null)
            {
                await Console.Error.WriteLineAsync($"fleet: fleetd is not reachable at {endpoint.Address}")
                    .ConfigureAwait(false);
                return 1;
            }

            stream = local;
        }

        var prefix = Prefix.Parse(Environment.GetEnvironmentVariable(PrefixVariable) ?? "ctrl+b");
        var code = await new AttachClient(stream, workspace, prefix, line => log.Write($"attach: {line}"))
            .RunAsync()
            .ConfigureAwait(false);

        if (code != 0 && sshHost is not null)
        {
            await Console.Error.WriteLineAsync(
                $"fleet: check that 'ssh {sshHost}' logs in without any prompt (a key or agent, and a " +
                "known host key), and that 'fleet' is on the remote PATH or named by FLEET_REMOTE_COMMAND.")
                .ConfigureAwait(false);
        }

        return code;
    }

    public static async Task<int> BridgeAsync()
    {
        var endpoint = Endpoint.Default();
        var local = await TryConnectAsync(endpoint).ConfigureAwait(false);

        if (local is null && await StartDaemonAsync().ConfigureAwait(false))
        {
            for (var i = 0; i < 50 && local is null; i++)
            {
                await Task.Delay(100).ConfigureAwait(false);
                local = await TryConnectAsync(endpoint).ConfigureAwait(false);
            }
        }

        if (local is null)
        {
            await Console.Error.WriteLineAsync($"fleet: fleetd is not reachable at {endpoint.Address}")
                .ConfigureAwait(false);
            return 1;
        }

        await using (local.ConfigureAwait(false))
        {
            var stdin = Console.OpenStandardInput();
            var stdout = Console.OpenStandardOutput();

            var up = stdin.CopyToAsync(local);
            var down = local.CopyToAsync(stdout);
            await Task.WhenAny(up, down).ConfigureAwait(false);
        }

        return 0;
    }

    public static async Task<bool> StartDaemonAsync()
    {
        if (!Ready)
        {
            return false;
        }

        var start = OperatingSystem.IsWindows() || !Adapters.OnPath("setsid")
            ? new ProcessStartInfo(Adapters.Executable) { ArgumentList = { "daemon" } }
            : new ProcessStartInfo("setsid") { ArgumentList = { "-f", Adapters.Executable, "daemon" } };

        start.UseShellExecute = false;
        start.CreateNoWindow = true;

        if (!OperatingSystem.IsWindows())
        {
            start.RedirectStandardInput = true;
            start.RedirectStandardOutput = true;
            start.RedirectStandardError = true;
        }

        try
        {
            using var process = Process.Start(start);
            await Task.Yield();
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static Stream Ssh(string host)
    {
        var remote = Environment.GetEnvironmentVariable(RemoteCommandVariable) ?? "fleet";
        var start = new ProcessStartInfo("ssh")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = false,
        };

        start.ArgumentList.Add("-T");
        start.ArgumentList.Add("-o");
        start.ArgumentList.Add("BatchMode=yes");
        start.ArgumentList.Add(host);
        start.ArgumentList.Add(remote);
        start.ArgumentList.Add("bridge");

        var process = Process.Start(start) ?? throw new IOException("could not start ssh");
        return new DuplexStream(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, process);
    }

    private static async Task<Stream?> TryConnectAsync(Endpoint endpoint)
    {
        try
        {
            return await endpoint.ConnectAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or TimeoutException or System.Net.Sockets.SocketException
                                      or OperationCanceledException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
