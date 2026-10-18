using Terminal.Gui.Drivers;

namespace Fleet.Tests.Ui;

// Tests that Init a Terminal.Gui driver must not grab the real console. On a Windows runner the driver
// otherwise opens CONIN$ and its input thread can block in ReadFile forever, hanging the whole run.
public sealed class NoRealConsoleTests
{
    [Fact]
    public void Terminal_gui_never_sees_a_real_terminal_in_tests() =>
        Assert.False(Driver.IsAttachedToTerminal(out _, out _));
}
