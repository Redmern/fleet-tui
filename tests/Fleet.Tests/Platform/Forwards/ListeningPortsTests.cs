using Fleet.Platform.Forwards;
using Fleet.Platform.Forwards.Models;

namespace Fleet.Tests.Platform.Forwards;

public sealed class ListeningPortsTests
{
    [Fact]
    public void Ss_lines_give_port_address_and_process()
    {
        const string ss = """
            LISTEN 0      511        127.0.0.1:5173      0.0.0.0:*    users:(("node",pid=812,fd=21))
            LISTEN 0      4096          [::1]:8080         [::]:*
            LISTEN 0      128         0.0.0.0:22         0.0.0.0:*
            LISTEN 0      4096   127.0.0.53%lo:53         0.0.0.0:*
            LISTEN 0      4096              *:9000             *:*    users:(("sshd",pid=1,fd=3))
            """;

        var ports = ListeningPorts.Parse(ss);

        Assert.Equal(
            [
                new ListeningPort(5173, "127.0.0.1", "node"),
                new ListeningPort(8080, "::1"),
                new ListeningPort(22, "0.0.0.0"),
                new ListeningPort(53, "127.0.0.53"),
                new ListeningPort(9000, "*", "sshd"),
            ],
            ports);
    }

    [Fact]
    public void Proc_net_tcp_gives_only_listening_sockets_with_decoded_addresses()
    {
        const string proc = """
              sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
               0: 0100007F:1435 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 1 1
               1: 00000000:1F90 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 2 1
               2: 0100007F:1F91 0100007F:C350 01 00000000:00000000 00:00000000 00000000  1000        0 3 1
              sl  local_address                         remote_address                        st
               0: 00000000000000000000000001000000:0BB8 00000000000000000000000000000000:0000 0A 0 0 0
               1: 00000000000000000000000000000000:0BB9 00000000000000000000000000000000:0000 0A 0 0 0
            """;

        var ports = ListeningPorts.Parse(proc);

        Assert.Equal(
            [
                new ListeningPort(5173, "127.0.0.1"),
                new ListeningPort(8080, "0.0.0.0"),
                new ListeningPort(3000, "::1"),
                new ListeningPort(3001, "::"),
            ],
            ports);
    }

    [Fact]
    public void System_ports_and_sshd_are_not_worth_forwarding()
    {
        var byPort = ListeningPorts.ByPort(
        [
            new ListeningPort(22, "0.0.0.0"),
            new ListeningPort(6010, "127.0.0.1", "sshd"),
            new ListeningPort(6011, "127.0.0.1", "sshd-session"),
            new ListeningPort(5173, "127.0.0.1", "node"),
            new ListeningPort(5173, "::1", "node"),
        ]);

        Assert.Equal([5173], byPort.Keys);
        Assert.Equal(["127.0.0.1", "::1"], byPort[5173]);
    }

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("0.0.0.0", "127.0.0.1")]
    [InlineData("::", "127.0.0.1")]
    [InlineData("::1", "[::1]")]
    [InlineData("10.0.0.5", "10.0.0.5")]
    [InlineData("fd00::5", "[fd00::5]")]
    public void The_forward_target_follows_where_the_app_listens(string address, string target) =>
        Assert.Equal(target, ListeningPorts.Target([address]));
}
