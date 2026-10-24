using Fleet.Cli.Composition;
using Fleet.Features.Uninstall.RunUninstall;
using Fleet.Features.Uninstall.RunUninstall.Models;
using Fleet.Platform.Uninstall;
using Fleet.Platform.Uninstall.Models;
using Fleet.Ports.Uninstall;

namespace Fleet.Tests.Cli;

public sealed class UninstallDaemonStopperTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), $"fleet-stopper-{Guid.NewGuid():N}");

    public UninstallDaemonStopperTests() => Directory.CreateDirectory(_home);

    public void Dispose() => Directory.Delete(_home, recursive: true);

    private sealed class NoUserPath : IUserPathVariable
    {
        public string? Get() => null;

        public void Set(string value)
        {
        }
    }

    private int Uninstall(IDaemonStopper stopper, List<string> output)
    {
        var targets = new FleetUninstallTargets(
            new UninstallEnvironment(
                Path.Combine(_home, "gone"), _home, Path.Combine(_home, ".config", "fleet"), false, _ => null),
            stopper,
            new NoUserPath());

        return new RunUninstallHandler(targets).Handle(new RunUninstallCommand(true, false), () => true, output.Add);
    }

    [Fact]
    public void An_endpoint_too_long_for_a_unix_socket_counts_as_a_daemon_that_is_not_running()
    {
        var stopper = new Adapters.EmbeddedDaemonStopper(
            () => throw new ArgumentOutOfRangeException("path", "invalid length for use with domain sockets"));

        Assert.True(stopper.Stop());
    }

    [Fact]
    public void An_endpoint_that_cannot_be_built_lets_the_uninstall_finish_with_exit_code_zero()
    {
        var output = new List<string>();
        var stopper = new Adapters.EmbeddedDaemonStopper(() => throw new ArgumentException("bad endpoint"));

        Assert.Equal(0, Uninstall(stopper, output));
        Assert.Contains(output, l => l.StartsWith("removed the fleet daemon", StringComparison.Ordinal));
    }

    [Fact]
    public void A_daemon_that_does_not_stop_is_still_a_failure()
    {
        var output = new List<string>();

        Assert.Equal(1, Uninstall(new Adapters.EmbeddedDaemonStopper(() => 1), output));
        Assert.Contains(output, l => l.StartsWith("could not remove the fleet daemon", StringComparison.Ordinal));
    }

    [Fact]
    public void Any_other_error_while_stopping_the_daemon_is_still_a_failure()
    {
        var output = new List<string>();
        var stopper = new Adapters.EmbeddedDaemonStopper(() => throw new InvalidOperationException("daemon hung"));

        Assert.Equal(1, Uninstall(stopper, output));
        Assert.Contains(output, l => l.Contains("daemon hung", StringComparison.Ordinal));
    }
}
