using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared.Iso;
using Fleet.Shared.Iso.Models;
using Fleet.Shared.Viewing;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed partial class RemoteLinkTests
{
    private volatile IsoConfig _farIso = IsoConfig.Off;

    public static TheoryData<string> ForwardAndStackOps =>
    [
        ForwardHub.AddOp,
        ForwardHub.RemoveOp,
        ForwardHub.StackStartOp,
        ForwardHub.StackStopOp,
    ];

    [Theory]
    [MemberData(nameof(ForwardAndStackOps))]
    public async Task With_iso_on_the_menu_the_remote_opened_for_a_remote_view_cannot_forward_or_stack_on_another_machine(string op)
    {
        var (far, _, window, _) = await ShowingHomelabAsync();
        await window.SendCommandAsync("menu");
        await Eventually(() => Task.FromResult(_farPanes.ByProgram("fleet") is not null));
        _farIso = IsoConfig.Off with { On = true };

        var response = await far.RequestAsync(new ControlRequest
        {
            Op = op,
            Host = "red@third",
            Port = 5173,
            Workspace = "proj",
            Caller = _farPanes.ByProgram("fleet")!.Env[FleetDaemon.PaneVariable],
        });

        AssertManagedWhereYouSit(response);
        Assert.Empty(_farOpened);
    }

    [Theory]
    [MemberData(nameof(ForwardAndStackOps))]
    public async Task With_iso_on_a_bridged_connection_keeps_the_iso_answer_for_forward_and_stack_ops(string op)
    {
        _farIso = IsoConfig.Off with { On = true };
        var viewer = await BridgedControlAsync();

        var response = await viewer.RequestAsync(new ControlRequest { Op = op, Host = "red@third", Port = 5173, Workspace = "proj" });

        Assert.False(response.Ok);
        Assert.Equal(IsoGuard.Refusal(IsoGuard.Forward), response.Error);
    }
}
