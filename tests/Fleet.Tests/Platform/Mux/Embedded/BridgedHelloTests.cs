using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class BridgedHelloTests
{
    [Theory]
    [InlineData("10.0.0.5 51234 10.0.0.9 22", "10.0.0.5")]
    [InlineData("fe80::1 51234 fe80::2 22", "fe80::1")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_origin_is_the_first_word_of_the_ssh_variable(string? variable, string? origin) =>
        Assert.Equal(origin, BridgedHello.Origin(variable));

    [Fact]
    public void A_stamp_overrides_whatever_the_client_claimed()
    {
        var hello = BridgedHello.Stamp(new Hello { Bridged = false, Origin = "trusted.lan" }, null, "10.0.0.5 51234 22");

        Assert.True(hello.Bridged);
        Assert.Equal("10.0.0.5", hello.Origin);
    }

    [Fact]
    public void A_stamp_carries_the_bridges_own_ssh_connection_over_the_clients_claim()
    {
        var hello = BridgedHello.Stamp(new Hello { Ssh = "1.1.1.1 1 2.2.2.2 22" }, "10.0.0.5 51234 10.0.0.9 2222", null);

        Assert.Equal("10.0.0.5 51234 10.0.0.9 2222", hello.Ssh);
    }

    [Fact]
    public void Without_ssh_variables_the_hello_is_bridged_from_nowhere()
    {
        var hello = BridgedHello.Stamp(new Hello { Origin = "trusted.lan" }, null, null);

        Assert.True(hello.Bridged);
        Assert.Null(hello.Origin);
    }

    [Fact]
    public async Task Forwarding_rewrites_the_first_hello_only()
    {
        using var input = new MemoryStream();
        var writer = new Wire(input);
        await writer.SendAsync(MessageType.Hello, new Hello { Version = 1, Role = ClientRoles.Attach }, WireJsonContext.Default.Hello);
        input.Position = 0;
        using var output = new MemoryStream();

        Assert.True(await BridgedHello.ForwardAsync(input, output, "10.0.0.5 1 2 22", null));

        output.Position = 0;
        var sent = await new Wire(output).ReceiveAsync();
        var hello = Wire.Read(sent!.Value.Payload, WireJsonContext.Default.Hello);
        Assert.Equal(MessageType.Hello, sent.Value.Type);
        Assert.True(hello.Bridged);
        Assert.Equal("10.0.0.5", hello.Origin);
        Assert.Equal(ClientRoles.Attach, hello.Role);
    }

    [Fact]
    public async Task An_empty_input_forwards_nothing()
    {
        using var input = new MemoryStream();
        using var output = new MemoryStream();

        Assert.False(await BridgedHello.ForwardAsync(input, output, null, null));
        Assert.Equal(0, output.Length);
    }
}
