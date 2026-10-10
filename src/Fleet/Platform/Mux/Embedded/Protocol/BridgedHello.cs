namespace Fleet.Platform.Mux.Embedded.Protocol;

public static class BridgedHello
{
    public const string SshConnectionVariable = "SSH_CONNECTION";

    public const string SshClientVariable = "SSH_CLIENT";

    public static Hello Stamp(Hello hello, string? sshConnection, string? sshClient)
    {
        hello.Bridged = true;
        hello.Origin = Origin(sshConnection) ?? Origin(sshClient);
        return hello;
    }

    public static async Task<bool> ForwardAsync(
        Stream from, Stream to, string? sshConnection, string? sshClient, CancellationToken ct = default)
    {
        var incoming = new Wire(from);

        if (await incoming.ReceiveAsync(ct).ConfigureAwait(false) is not { } first)
        {
            return false;
        }

        var outgoing = new Wire(to);

        if (first.Type == MessageType.Hello)
        {
            var hello = Stamp(Wire.Read(first.Payload, WireJsonContext.Default.Hello), sshConnection, sshClient);
            await outgoing.SendAsync(MessageType.Hello, hello, WireJsonContext.Default.Hello, ct).ConfigureAwait(false);
        }
        else
        {
            await outgoing.SendAsync(first.Type, first.Payload, ct).ConfigureAwait(false);
        }

        return true;
    }

    public static string? Origin(string? variable) =>
        variable?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [var address, ..]
            ? address
            : null;
}
