using Fleet.Cli.Composition;
using Fleet.Features.Shim.ServeClipboard;
using Fleet.Platform.Clipboard;

namespace Fleet.Tests.Cli;

public sealed class DaemonStartTests
{
    [Fact]
    public void Only_a_daemon_started_for_a_bridge_is_marked_as_serving_remote_viewers()
    {
        var bridge = EmbeddedWiring.DaemonStart(forBridge: true);
        var local = EmbeddedWiring.DaemonStart(forBridge: false);

        Assert.Equal("1", bridge.Environment[ShimFolder.RemoteDaemonVariable]);
        Assert.False(local.Environment.ContainsKey(ShimFolder.RemoteDaemonVariable));
    }

    [Theory]
    [InlineData("xclip")]
    [InlineData("wl-paste")]
    [InlineData("wl-copy")]
    public void Remote_panes_get_a_shim_script_for_each_clipboard_tool(string tool) =>
        Assert.Contains(tool, EmbeddedWiring.ShimTools);

    [Fact]
    public void Installing_the_shims_writes_a_script_for_every_copy_tool()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var folder = Path.Combine(Path.GetTempPath(), $"fleet-shims-{Guid.NewGuid():N}");
        try
        {
            var scripts = EmbeddedWiring.ShimTools.ToDictionary(t => t, t => ShimScripts.Script("/opt/fleet", t));

            Assert.True(ShimFolder.Install(folder, scripts, _ => { }));

            foreach (var tool in (string[])["wl-copy", "xclip"])
            {
                Assert.Contains($" shim {tool} ", File.ReadAllText(Path.Combine(folder, tool)));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_daemon_never_inherits_the_ssh_session_it_was_started_from()
    {
        var start = EmbeddedWiring.DaemonStart(forBridge: false);

        Assert.False(start.Environment.ContainsKey("SSH_CONNECTION"));
        Assert.False(start.Environment.ContainsKey("SSH_CLIENT"));
        Assert.False(start.Environment.ContainsKey("SSH_TTY"));
    }
}
