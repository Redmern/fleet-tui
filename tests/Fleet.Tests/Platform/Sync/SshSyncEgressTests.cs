using System.Diagnostics;
using Fleet.Platform.Sync;
using Fleet.Ports;
using Fleet.Ports.Settings;
using Fleet.Ports.Sync.Exceptions;
using Fleet.Ports.Sync.Models;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Platform.Sync;

public sealed class SshSyncEgressTests
{
    public static TheoryData<string> Methods => ["repo", "paths", "secrets"];

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Machine_iso_refuses_every_send_before_anything_is_spawned(string method)
    {
        var (egress, _, _, log, runner) = Build(machineIso: true);

        var refused = await Assert.ThrowsAsync<SyncRefusedException>(() => Send(egress, method));

        Assert.Equal(SyncIso.MachineRefusal, refused.Message);
        Assert.Equal(0, runner.Starts);
        Assert.Contains(log.Lines, l => l.Contains("sync_to_remote refused", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Project_iso_refuses_every_send_before_anything_is_spawned(string method)
    {
        var (egress, _, _, log, runner) = Build(project: SettingsConfig.Default.WithIso(true));

        var refused = await Assert.ThrowsAsync<SyncRefusedException>(() => Send(egress, method));

        Assert.Contains("techweb", refused.Message, StringComparison.Ordinal);
        Assert.Equal(0, runner.Starts);
        Assert.Single(log.Lines);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Machine_iso_still_refuses_when_the_project_has_iso_off(string method)
    {
        var (egress, _, settings, _, runner) = Build(machineIso: true, project: SettingsConfig.Default.WithIso(false));

        await Assert.ThrowsAsync<SyncRefusedException>(() => Send(egress, method));

        Assert.False(settings.Config.Iso);
        Assert.Equal(0, runner.Starts);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task A_project_allow_rule_does_not_override_machine_iso(string method)
    {
        var allowed = SettingsConfig.Default.With(HarnessTool.SyncToRemote, ActionPolicy.Allow);
        var (egress, _, _, _, runner) = Build(machineIso: true, project: allowed);

        await Assert.ThrowsAsync<SyncRefusedException>(() => Send(egress, method));

        Assert.Equal(0, runner.Starts);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task A_project_allow_rule_does_not_override_project_iso(string method)
    {
        var allowed = SettingsConfig.Default.With(HarnessTool.SyncToRemote, ActionPolicy.Allow).WithIso(true);
        var (egress, _, _, _, runner) = Build(project: allowed);

        await Assert.ThrowsAsync<SyncRefusedException>(() => Send(egress, method));

        Assert.Equal(0, runner.Starts);
    }

    [Fact]
    public async Task Machine_iso_is_checked_before_the_project_settings_are_even_read()
    {
        var (egress, _, settings, _, _) = Build(machineIso: true);

        await Assert.ThrowsAsync<SyncRefusedException>(() => Send(egress, "repo"));

        Assert.Equal(0, settings.Loads);
    }

    [Fact]
    public async Task A_forbid_rule_refuses_with_the_head_gate_wording()
    {
        var forbidden = SettingsConfig.Default.With(HarnessTool.SyncToRemote, ActionPolicy.Forbid);
        var (egress, _, _, log, runner) = Build(project: forbidden);

        var refused = await Assert.ThrowsAsync<SyncRefusedException>(() => Send(egress, "paths"));

        Assert.Equal(ToolRefusal.Forbidden("techweb", forbidden, HarnessTool.SyncToRemote), refused.Message);
        Assert.Equal(0, runner.Starts);
        Assert.Single(log.Lines);
    }

    [Theory]
    [MemberData(nameof(Methods))]
    public async Task Without_iso_a_send_is_not_implemented_yet_and_spawns_nothing(string method)
    {
        var (egress, _, _, log, runner) = Build();

        var result = await Send(egress, method);

        Assert.False(result.Succeeded);
        Assert.Contains("not implemented", result.Error, StringComparison.Ordinal);
        Assert.Equal(0, runner.Starts);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public void Iso_is_on_when_either_the_machine_or_the_project_has_it()
    {
        Assert.False(SyncIso.On(false, SettingsConfig.Default));
        Assert.True(SyncIso.On(true, SettingsConfig.Default));
        Assert.True(SyncIso.On(false, SettingsConfig.Default.WithIso(true)));
        Assert.True(SyncIso.On(true, SettingsConfig.Default.WithIso(false)));
    }

    [Fact]
    public void Sync_to_remote_is_a_tool_that_asks_by_default()
    {
        Assert.Equal("sync_to_remote", HarnessToolIds.For(HarnessTool.SyncToRemote));
        Assert.Equal(HarnessTool.SyncToRemote, HarnessToolIds.Parse("sync_to_remote"));
        Assert.Equal(ActionPolicy.Ask, SettingsConfig.Default.RuleFor(HarnessTool.SyncToRemote).Policy);
        Assert.Equal(ActionPolicy.Ask, SettingsConfig.Default.MergedOverDefaults().RuleFor(HarnessTool.SyncToRemote).Policy);
    }

    [Fact]
    public void Iso_is_part_of_the_settings_signature()
    {
        Assert.NotEqual(SettingsConfig.Default.Signature, SettingsConfig.Default.WithIso(true).Signature);
        Assert.True(SettingsConfig.Default.WithIso(true).MergedOverDefaults().Iso);
    }

    private static Task<Fleet.Shared.Results.Result> Send(SshSyncEgress egress, string method)
    {
        var request = new SyncRequest("box", "techweb", "/src/techweb", ["main"], "c-1", "cli");

        return method switch
        {
            "repo" => egress.SendRepoAsync(request),
            "paths" => egress.SendPathsAsync(request),
            "secrets" => egress.SendSecretsAsync(request),
            _ => throw new ArgumentOutOfRangeException(nameof(method)),
        };
    }

    private static (SshSyncEgress, IsoSwitch, FakeSettings, FakeLog, ExplodingRunner) Build(
        bool machineIso = false, SettingsConfig? project = null)
    {
        var machine = new IsoSwitch(on: machineIso);
        var settings = new FakeSettings { Config = project ?? SettingsConfig.Default };
        var log = new FakeLog();
        var runner = new ExplodingRunner();

        return (new SshSyncEgress(machine, settings, log, runner), machine, settings, log, runner);
    }

    private sealed class ExplodingRunner : ISyncProcessRunner
    {
        public int Starts { get; private set; }

        public Process Start(ProcessStartInfo start)
        {
            Starts++;
            Assert.Fail($"the egress spawned {start.FileName} {string.Join(' ', start.ArgumentList)}");
            throw new InvalidOperationException();
        }
    }

    private sealed class FakeSettings : ISettingsStore
    {
        public SettingsConfig Config { get; set; } = SettingsConfig.Default;

        public int Loads { get; private set; }

        public SettingsConfig Load(string project)
        {
            Loads++;
            return Config;
        }

        public void Save(string project, SettingsConfig config) => Config = config;
    }

    private sealed class FakeLog : IFleetLog
    {
        public List<string> Lines { get; } = [];

        public void Swallowed(Exception e) { }

        public void Write(string line) => Lines.Add(line);

        public IReadOnlyList<string> Tail(int lines) => Lines;
    }
}
