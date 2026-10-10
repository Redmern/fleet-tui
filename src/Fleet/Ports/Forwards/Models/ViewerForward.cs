namespace Fleet.Ports.Forwards.Models;

public sealed record ViewerForward(string? Viewer, string? SshConnection)
{
    public bool Sent => Viewer is not null;

    public string? SshCommand(int port, string user) =>
        SshConnection?.Split(' ', StringSplitOptions.RemoveEmptyEntries) is [_, _, var host, var sshPort]
            ? $"ssh -N -L {port}:localhost:{port}{(sshPort == "22" ? string.Empty : $" -p {sshPort}")} {user}@{host}"
            : null;

    public string Describe(int port, bool forward, string user) =>
        Viewer is { } viewer
            ? forward
                ? $"{viewer} forwards {port} and opens http://localhost:{port} in its browser."
                : $"{viewer} stops forwarding {port}."
            : SshCommand(port, user) is { } command
                ? $"no fleet views this machine; the user reached it by ssh, so on their machine they run: {command}"
                : $"no machine views this one; port {port} is on localhost here already.";
}
