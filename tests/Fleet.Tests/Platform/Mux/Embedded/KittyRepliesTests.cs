using System.Text;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Input;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class KittyRepliesTests
{
    [Theory]
    [InlineData("\e[?1u", "")]
    [InlineData("\e[?0u", "")]
    [InlineData("\e[?64;1c\e[?15u\e[1;1R", "\e[?64;1c\e[1;1R")]
    [InlineData("\e[?64;1c", "\e[?64;1c")]
    public void The_kitty_flags_report_is_taken_out_of_a_reply_and_the_rest_kept(string reply, string kept)
    {
        Assert.Equal(kept, Encoding.Latin1.GetString(KittyReplies.Without(Encoding.Latin1.GetBytes(reply))));
    }

    [Fact]
    public async Task A_conpty_pane_never_tells_its_program_that_kitty_keys_are_available()
    {
        var panes = new FakePanes();
        using var conpty = new PaneRuntime("p1", panes.NewPty(), panes.NewTerminal, 80, 24);
        conpty.Feed("\e[?9001h"u8.ToArray(), 8);

        conpty.Reply("\e[?1u\e[0n"u8.ToArray());

        Assert.Equal("\e[0n", await FirstWritten((FakePanes.FakePty)conpty.Pty));
    }

    [Fact]
    public async Task Other_panes_keep_the_kitty_answer()
    {
        var panes = new FakePanes();
        using var unix = new PaneRuntime("p1", panes.NewPty(), panes.NewTerminal, 80, 24);

        unix.Reply("\e[?1u"u8.ToArray());

        Assert.Equal("\e[?1u", await FirstWritten((FakePanes.FakePty)unix.Pty));
    }

    private static async Task<string> FirstWritten(FakePanes.FakePty pty)
    {
        await pty.FirstWrite.WaitAsync(TimeSpan.FromSeconds(10));
        return pty.Written;
    }
}
