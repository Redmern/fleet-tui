using Fleet.Ui;

namespace Fleet.Tests.Ui;

public sealed class TerminalClipboardTests
{
    [Fact]
    public void Copying_writes_an_osc_52_sequence_that_fleetd_hands_to_the_viewers_clipboard()
    {
        using var written = new StringWriter();

        TerminalClipboard.Copy("ssh -N -L 1:localhost:1 red@box", written);

        Assert.Equal("\e]52;c;c3NoIC1OIC1MIDE6bG9jYWxob3N0OjEgcmVkQGJveA==\a", written.ToString());
    }
}
