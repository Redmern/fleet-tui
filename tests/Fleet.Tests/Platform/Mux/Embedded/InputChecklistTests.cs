using System.Text;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class InputChecklistTests
{
    private const uint RightAlt = 0x01;
    private const uint LeftAlt = 0x02;
    private const uint LeftCtrl = 0x08;
    private const uint Shift = 0x10;

    private static WindowsConsole.KeyEventRecord Record(ushort vk, char uc, uint state, bool down = true, ushort repeat = 1) =>
        new()
        {
            KeyDown = down ? 1 : 0,
            RepeatCount = repeat,
            VirtualKeyCode = vk,
            UnicodeChar = uc,
            ControlKeyState = state,
        };

    private static List<KeyInput> Translate(params WindowsConsole.KeyEventRecord[] records)
    {
        var keys = new WindowsKeys(vk => vk == 0xDE);
        return records.SelectMany(keys.Translate).ToList();
    }

    [Fact]
    public void Shift_tab_and_shift_enter_keep_their_modifier()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tab = Translate(Record(0x09, '\t', Shift)).Single();
        var enter = Translate(Record(0x0D, '\r', Shift)).Single();

        Assert.Equal((Key.Tab, Mods.Shift), (tab.Key, tab.Mods & Mods.Shift));
        Assert.Equal((Key.Enter, Mods.Shift), (enter.Key, enter.Mods & Mods.Shift));
    }

    [Fact]
    public void Escape_is_its_own_key_at_once()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var escape = Translate(Record(0x1B, '\e', 0)).Single();

        Assert.Equal((Key.Escape, KeyAction.Press), (escape.Key, escape.Action));
    }

    [Fact]
    public void Alt_letters_keep_alt_and_the_case_shift_gave_them()
    {
        var alt = Translate(Record(0x41, 'a', LeftAlt)).Single();
        var altShift = Translate(Record(0x41, 'A', LeftAlt | Shift)).Single();
        var ctrlAlt = Translate(Record(0x41, '\x01', LeftAlt | LeftCtrl)).Single();

        Assert.Equal((Key.A, Mods.Alt, "a"), (alt.Key, alt.Mods, alt.Utf8));
        Assert.Equal((Mods.Alt | Mods.Shift, "A"), (altShift.Mods, altShift.Utf8));
        Assert.Equal((Mods.Alt | Mods.Ctrl, (string?)null), (ctrlAlt.Mods, ctrlAlt.Utf8));
    }

    [Theory]
    [InlineData((ushort)0x48, 'h', "smart-resize left")]
    [InlineData((ushort)0x4A, 'j', "smart-resize down")]
    [InlineData((ushort)0x4B, 'k', "smart-resize up")]
    [InlineData((ushort)0x4C, 'l', "smart-resize right")]
    public void Alt_hjkl_console_records_resize_by_default(ushort vk, char letter, string command)
    {
        var alt = Translate(Record(vk, letter, LeftAlt)).Single();
        var ctrl = Translate(Record(vk, (char)(letter - 'a' + 1), LeftCtrl)).Single();

        Assert.Equal(command, MuxKeys.Defaults.DirectCommand(alt.Key, alt.Mods, alt.Utf8));
        Assert.Equal(command.Replace("smart-resize", "smart-focus"), MuxKeys.Defaults.DirectCommand(ctrl.Key, ctrl.Mods, ctrl.Utf8));
    }

    [Fact]
    public void Right_alt_is_altgr_only_with_left_ctrl()
    {
        var altGr = Translate(Record(0x51, '@', RightAlt | LeftCtrl)).Single();
        var rightAlt = Translate(Record(0x41, 'a', RightAlt)).Single();

        Assert.Equal((Mods.None, "@"), (altGr.Mods & (Mods.Ctrl | Mods.Alt), altGr.Utf8));
        Assert.Equal(Mods.Alt, rightAlt.Mods & (Mods.Ctrl | Mods.Alt));
    }

    [Fact]
    public void Ctrl_letters_are_keys_with_ctrl_and_ctrl_j_is_not_enter()
    {
        var ctrlS = Translate(Record(0x53, '\x13', LeftCtrl)).Single();
        var ctrlJ = Translate(Record(0x4A, '\n', LeftCtrl)).Single();

        Assert.Equal((Key.S, Mods.Ctrl, (string?)null), (ctrlS.Key, ctrlS.Mods, ctrlS.Utf8));
        Assert.Equal((Key.J, Mods.Ctrl), (ctrlJ.Key, ctrlJ.Mods));
    }

    [Fact]
    public void A_dead_key_gives_no_text_and_the_composed_character_arrives_once()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var keys = Translate(
            Record(0xDE, '\0', 0),
            Record(0xDE, '\0', 0, down: false),
            Record(0x45, 'é', 0),
            Record(0x45, 'é', 0, down: false));

        Assert.Equal("é", string.Concat(keys.Where(k => k.Action == KeyAction.Press).Select(k => k.Utf8)));
        Assert.Equal((Key.Unidentified, (string?)null, 0u), (keys[0].Key, keys[0].Utf8, keys[0].Unshifted));
        Assert.False(keys[0].IsRawText);
    }

    [Fact]
    public void A_surrogate_pair_from_the_emoji_picker_becomes_one_character()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var keys = Translate(
            Record(0xE7, '\uD83D', 0),
            Record(0xE7, '\uD83D', 0, down: false),
            Record(0xE7, '\uDE00', 0),
            Record(0xE7, '\uDE00', 0, down: false));

        Assert.Equal("😀", string.Concat(keys.Where(k => k.Action == KeyAction.Press).Select(k => k.Utf8)));
    }

    [Fact]
    public void A_repeat_count_is_kept_as_one_press_and_its_repeats()
    {
        var keys = Translate(Record(0x58, 'x', 0, repeat: 3));

        Assert.Equal([KeyAction.Press, KeyAction.Repeat, KeyAction.Repeat], keys.Select(k => k.Action));
    }

    [Fact]
    public void A_ctrl_or_alt_chord_is_never_taken_for_a_paste()
    {
        WindowsConsole.InputRecord Key(ushort vk, char uc, uint state) =>
            new() { EventType = WindowsConsole.KeyEvent, Key = Record(vk, uc, state) };

        Assert.False(PasteBurst.Starts([Key(0x41, 'a', LeftAlt), Key(0x42, 'b', LeftAlt)]));
        Assert.False(PasteBurst.Starts([Key(0x53, '\x13', LeftCtrl), Key(0x53, '\x13', LeftCtrl)]));
    }

    [Fact]
    public void Leaving_resets_every_mode_fleet_or_a_pane_can_have_turned_on()
    {
        var restore = AttachClient.RestoreSequence;

        foreach (var reset in new[]
                 {
                     "\e[?1000l", "\e[?1002l", "\e[?1003l", "\e[?1006l", "\e[?1004l", "\e[?2004l",
                     "\e[?1l", "\e>", "\e[0 q", "\e[?25h", "\e[?1049l", "\e[?2026l", "\e[0m",
                 })
        {
            Assert.Contains(reset, restore, StringComparison.Ordinal);
        }

        Assert.EndsWith(OperatingSystem.IsWindows() ? "\e[?1049l" : "\e[23;0t", restore, StringComparison.Ordinal);
    }

    [Fact]
    public void A_host_reply_split_across_reads_passes_through_whole_and_in_order()
    {
        var sgr = new SgrMouse();

        var first = sgr.Feed("abc\e"u8.ToArray());
        var second = sgr.Feed("]11;rgb:1e1e/1e1e/2e2e\e\\z"u8.ToArray());

        var bytes = first.Concat(second).OfType<byte[]>().SelectMany(b => b).ToArray();
        Assert.Equal("abc\e]11;rgb:1e1e/1e1e/2e2e\e\\z", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain(first.Concat(second), item => item is MouseMessage);
    }
}
