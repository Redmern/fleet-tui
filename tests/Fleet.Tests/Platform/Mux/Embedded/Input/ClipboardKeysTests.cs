using System.Text;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Tests.Platform.Mux.Embedded.Input;

public class ClipboardKeysTests
{
    private const uint LeftCtrl = 0x0008;
    private const uint Shift = 0x0010;

    private static KeyInput Windows(char letter, uint state)
    {
        var record = new WindowsConsole.KeyEventRecord
        {
            KeyDown = 1,
            RepeatCount = 1,
            VirtualKeyCode = letter,
            UnicodeChar = (char)(letter - 'A' + 1),
            ControlKeyState = state,
        };

        return Assert.Single(new WindowsKeys(_ => false).Translate(record));
    }

    private static string? Direct(MuxKeys keys, KeyInput input) => keys.DirectCommand(input.Key, input.Mods, input.Utf8);

    private static (string? Command, int Length) Unix(MuxKeys keys, string typed) =>
        keys.DirectBytes(ModifiedKeys.Normalize(Encoding.ASCII.GetBytes(typed)));

    [Theory]
    [InlineData('C', "copy")]
    [InlineData('V', "paste")]
    public void Ctrl_shift_c_and_v_copy_and_paste_on_windows(char letter, string command)
    {
        Assert.Equal(command, Direct(MuxKeys.Defaults, Windows(letter, LeftCtrl | Shift)));
    }

    [Theory]
    [InlineData('C')]
    [InlineData('V')]
    public void Ctrl_c_and_ctrl_v_stay_with_the_pane_on_windows(char letter)
    {
        Assert.Null(Direct(MuxKeys.Defaults, Windows(letter, LeftCtrl)));
    }

    [Theory]
    [InlineData("\e[27;6;67~", "copy")]
    [InlineData("\e[27;6;99~", "copy")]
    [InlineData("\e[99;6u", "copy")]
    [InlineData("\e[67;6u", "copy")]
    [InlineData("\e[27;6;86~", "paste")]
    [InlineData("\e[118;6u", "paste")]
    public void Ctrl_shift_c_and_v_copy_and_paste_when_the_terminal_reports_them(string typed, string command)
    {
        var (bound, length) = Unix(MuxKeys.Defaults, typed);

        Assert.Equal(command, bound);
        Assert.Equal(ModifiedKeys.Normalize(Encoding.ASCII.GetBytes(typed)).Length, length);
    }

    [Theory]
    [InlineData("\u0003")]
    [InlineData("\u0016")]
    public void Ctrl_c_and_ctrl_v_stay_with_the_pane_on_unix(string typed)
    {
        Assert.Equal((null, 0), Unix(MuxKeys.Defaults, typed));
    }

    [Fact]
    public void The_clipboard_keys_can_be_moved_or_unbound_in_the_keys_file()
    {
        var keys = MuxKeys.From(
            new MuxKeysFile { Keys = new() { ["ctrl+shift+c"] = "none", ["ctrl+alt+c"] = "copy" } }, null);

        Assert.Null(Direct(keys, Windows('C', LeftCtrl | Shift)));
        Assert.Equal("copy", keys.DirectCommand(Key.C, Mods.Ctrl | Mods.Alt, null));
        Assert.Equal("paste", Direct(keys, Windows('V', LeftCtrl | Shift)));
    }
}
