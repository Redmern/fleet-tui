using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class AttachForwardEffectsTests
{
    private readonly List<(bool Forward, int Port)> _asked = [];

    [Fact]
    public void Forward_and_unforward_effects_reach_the_attach_clients_tunnels()
    {
        Assert.True(AttachClient.Forward(new HostEffect { Kind = HostEffects.ForwardPort, Value = "5173" }, Ask));
        Assert.True(AttachClient.Forward(new HostEffect { Kind = HostEffects.UnforwardPort, Value = "5173" }, Ask));

        Assert.Equal([(true, 5173), (false, 5173)], _asked);
    }

    [Fact]
    public void Other_effects_and_bad_ports_are_left_alone()
    {
        Assert.False(AttachClient.Forward(new HostEffect { Kind = HostEffects.Bell }, Ask));
        Assert.False(AttachClient.Forward(new HostEffect { Kind = HostEffects.ForwardPort, Value = "nope" }, Ask));
        Assert.False(AttachClient.Forward(new HostEffect { Kind = HostEffects.ForwardPort, Value = "5173" }, null));

        Assert.Empty(_asked);
    }

    private void Ask(bool forward, int port) => _asked.Add((forward, port));
}
