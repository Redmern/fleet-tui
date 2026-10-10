using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Fleet.Platform.Forwards;
using Fleet.Platform.Forwards.Models;
using Fleet.Ports.Forwards.Enums;

namespace Fleet.Tests.Platform.Forwards;

// Runs only against a real sshd: set FLEET_SSH_IT_CONFIG to an ssh config file and FLEET_SSH_IT_HOST to a host in
// it that logs in without a prompt (e.g. a throwaway user-level sshd on 127.0.0.1). The "remote" is then this
// machine, so the remote app's port is busy locally and the forward lands on another local port.
public sealed class RealSshTests
{
    private static readonly string? Config = Environment.GetEnvironmentVariable("FLEET_SSH_IT_CONFIG");

    private static readonly string Host = Environment.GetEnvironmentVariable("FLEET_SSH_IT_HOST") ?? "fleettest";

    private static Task<SshResult> Ssh(IReadOnlyList<string> args, CancellationToken ct) =>
        SshProcess.RunAsync(["-F", Config!, .. args], ct);

    [RealSshFact]
    public async Task A_real_master_forwards_a_listening_port_and_cancels_it()
    {
        var directory = ControlPaths.Prepare(Path.Combine(Path.GetTempPath(), $"fleet-it-{Environment.ProcessId}"));
        var socket = ControlPaths.For(directory, Host);
        Assert.True(ControlPaths.Fits(socket));

        using var app = new TcpListener(IPAddress.Loopback, 0);
        app.Start();
        var remote = ((IPEndPoint)app.LocalEndpoint).Port;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                using var client = await app.AcceptTcpClientAsync();
                await client.GetStream().WriteAsync("HTTP/1.0 200 OK\r\n\r\nhello"u8.ToArray());
            }
        });

        var master = new ProcessStartInfo("ssh") { RedirectStandardInput = true, RedirectStandardError = true };
        foreach (var arg in (string[])["-F", Config!, "-T", .. SshControl.MasterOptions(socket), Host, "sleep 60"])
        {
            master.ArgumentList.Add(arg);
        }

        using var link = Process.Start(master)!;
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!(await Ssh(SshControl.Check(socket, Host), default)).Ok)
            {
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(15), "the master did not come up");
                await Task.Delay(200);
            }

            var forwards = new HostForwards(Host, socket, LocalPorts.Loopback(), Ssh, _ => { });
            var listening = await forwards.ScanAsync(default);
            Assert.NotNull(listening);
            Assert.Contains(remote, listening!.Keys);

            await forwards.ReconcileAsync(listening, new Dictionary<int, string> { [remote] = "it" }, default);
            var row = forwards.Row(remote);
            Assert.Equal(ForwardState.Forwarded, row.State);
            Assert.NotEqual(remote, row.LocalPort);

            using (var http = new HttpClient())
            {
                Assert.Equal("hello", await http.GetStringAsync(row.Url));
            }

            await forwards.UnforwardNowAsync(remote, default);
            Assert.True(LocalPorts.BindsOnLoopback(row.LocalPort!.Value));
        }
        finally
        {
            await Ssh(SshControl.Exit(socket, Host), default);
            if (!link.HasExited)
            {
                link.Kill();
            }

            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class RealSshFactAttribute : FactAttribute
    {
        public RealSshFactAttribute()
        {
            if (Config is null)
            {
                Skip = "set FLEET_SSH_IT_CONFIG (and FLEET_SSH_IT_HOST) to run against a real sshd";
            }
        }
    }
}
