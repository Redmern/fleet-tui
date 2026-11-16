using Fleet.Features.Mcp.ServeMcp;
using Fleet.Platform.Approvals;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Ports.Approvals.Enums;
using Fleet.Ports.Mcp.Models;
using Fleet.Ports.Settings;
using Fleet.Shared.Mcp;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Mcp;

public sealed class LateApprovalTests : ConfigHomeFixture
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private DateTimeOffset _now = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_answer_after_the_client_deadline_does_not_run_the_action()
    {
        var channel = new FileApprovalChannel(
            poll: TimeSpan.FromMilliseconds(5),
            timeout: TimeSpan.FromSeconds(120),
            now: () => _now);
        channel.Heartbeat("techweb");
        var performed = 0;
        var dispatcher = new McpDispatcher(
            McpCaller.AtRoot("techweb", "C:/repos/techweb") with { Caller = "agent:backend/login" },
            new AskingSettings(),
            channel,
            new QuietLog(),
            (_, _) =>
            {
                performed++;
                return Task.FromResult(McpResult.Ok("done"));
            },
            approvalTimeout: () => TimeSpan.FromSeconds(55),
            time: new Clock(() => _now));

        var handling = dispatcher.HandleAsync(new McpRequest("new_agent", new Dictionary<string, string>()));
        var pending = await Spin(channel);

        _now += TimeSpan.FromSeconds(56);
        var result = await handling.WaitAsync(Patience);
        channel.Answer("techweb", pending.Id, ApprovalDecision.Allowed);

        Assert.True(result.IsError);
        Assert.Equal(
            "No one answered the approval prompt (shown over agent:backend/login in techweb) within 55 s",
            result.Text);
        Assert.Equal(0, performed);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(FleetPaths.Approvals, "techweb")));
    }

    private static async Task<Fleet.Ports.Approvals.Models.PendingApproval> Spin(FileApprovalChannel channel)
    {
        using var cancel = new CancellationTokenSource(Patience);

        while (true)
        {
            if (channel.TakePending("techweb") is { } pending)
            {
                return pending;
            }

            await Task.Yield();
            cancel.Token.ThrowIfCancellationRequested();
        }
    }

    private sealed class Clock(Func<DateTimeOffset> now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now();
    }

    private sealed class AskingSettings : ISettingsStore
    {
        public SettingsConfig Load(string project) =>
            SettingsConfig.Default.With(HarnessTool.NewAgent, ActionPolicy.Ask);

        public void Save(string project, SettingsConfig config)
        {
        }
    }

    private sealed class QuietLog : IFleetLog
    {
        public void Swallowed(Exception e)
        {
        }

        public void Write(string line)
        {
        }

        public IReadOnlyList<string> Tail(int lines) => [];
    }
}
