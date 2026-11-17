using System.Runtime.Versioning;
using Fleet.Cli;
using Fleet.Cli.Commands;
using Fleet.Cli.Composition;
using Fleet.Ports;
using Fleet.Shared;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Cli;

public sealed class IncludeWiringTests : IDisposable
{
    private readonly string _config = Path.Combine(Path.GetTempPath(), "fleet-include-wiring\\" + Path.GetRandomFileName());
    private readonly IDisposable _scope;
    private readonly Log _log = new();

    public IncludeWiringTests() => _scope = FleetHome.Scope(_config);

    public void Dispose()
    {
        _scope.Dispose();

        if (Directory.Exists(_config))
        {
            Directory.Delete(_config, recursive: true);
        }
    }

    [Fact]
    public void The_include_goes_into_the_fleet_config_home()
    {
        Assert.Equal(Path.Combine(_config, OrchestratorInclude.FileName), IncludeWiring.IncludePath);
        Assert.Null(IncludeWiring.WriteHere());
        Assert.Equal(OrchestratorInclude.Text, File.ReadAllText(IncludeWiring.IncludePath));
    }

    [Fact]
    public void Daemon_start_writes_a_missing_or_stale_include_and_logs_it()
    {
        IncludeWiring.OnDaemonStart(_log);

        Assert.Equal(OrchestratorInclude.Text, File.ReadAllText(IncludeWiring.IncludePath));
        Assert.Contains(_log.Lines, l => l.Contains(IncludeWiring.IncludePath, StringComparison.Ordinal));

        File.WriteAllText(IncludeWiring.IncludePath, "an older fleet's text");
        IncludeWiring.OnDaemonStart(_log);

        Assert.Equal(OrchestratorInclude.Text, File.ReadAllText(IncludeWiring.IncludePath));
    }

    [Fact]
    public void Daemon_start_with_a_current_include_logs_nothing()
    {
        IncludeWiring.WriteHere();

        IncludeWiring.OnDaemonStart(_log);

        Assert.Empty(_log.Lines);
    }

    [Fact]
    public void A_new_binary_that_cannot_be_started_counts_as_not_written() =>
        Assert.False(IncludeWiring.InstalledWrites(Path.Combine(_config, "no-such-fleet")));

    [Fact]
    public void The_new_binary_is_run_as_setup_include_only_and_its_exit_code_decides()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var args = Path.Combine(_config, "args");

        Assert.True(IncludeWiring.InstalledWrites(Script($"printf '%s\\n' \"$@\" > '{args}'; exit 0")));
        Assert.Equal(["setup", IncludeWiring.IncludeOnlyFlag], File.ReadAllLines(args));
        Assert.False(IncludeWiring.InstalledWrites(Script("echo failed >&2; exit 3")));
    }

    [Fact]
    public void A_new_binary_that_hangs_is_stopped_and_counts_as_not_written()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var started = DateTime.UtcNow;

        Assert.False(IncludeWiring.InstalledWrites(Script("sleep 30"), TimeSpan.FromMilliseconds(300)));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(20));
    }

    [Fact]
    public void Setup_include_only_writes_the_include_and_nothing_else()
    {
        var code = SetupCommand.Run(CommandLine.Parse(["setup", IncludeWiring.IncludeOnlyFlag]));

        Assert.Equal(0, code);
        Assert.Equal([IncludeWiring.IncludePath], Directory.GetFiles(_config, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void Setup_include_only_fails_when_the_include_cannot_be_written()
    {
        Directory.CreateDirectory(_config);
        var blocker = Path.Combine(_config, "file");
        File.WriteAllText(blocker, "not a folder");

        using var scope = FleetHome.Scope(Path.Combine(blocker, "fleet"));

        Assert.Equal(1, SetupCommand.Run(CommandLine.Parse(["setup", IncludeWiring.IncludeOnlyFlag])));
    }

    [UnsupportedOSPlatform("windows")]
    private string Script(string body)
    {
        Directory.CreateDirectory(_config);
        var path = Path.Combine(_config, $"fleet-{Guid.NewGuid():N}");
        File.WriteAllText(path, $"#!/bin/sh\n{body}\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private sealed class Log : IFleetLog
    {
        public List<string> Lines { get; } = [];

        public void Swallowed(Exception e) => Lines.Add(e.Message);

        public void Write(string line) => Lines.Add(line);

        public IReadOnlyList<string> Tail(int lines) => Lines;
    }
}
