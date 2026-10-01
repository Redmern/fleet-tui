using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Host;
using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class PasteTests
{
    private const uint LeftCtrl = 0x0008;
    private const uint RightAlt = 0x0001;

    private static WindowsConsole.InputRecord Key(char c, bool down = true, uint state = 0, ushort vk = 0) => new()
    {
        EventType = WindowsConsole.KeyEvent,
        Key = new WindowsConsole.KeyEventRecord
        {
            KeyDown = down ? 1 : 0,
            RepeatCount = 1,
            VirtualKeyCode = vk != 0 ? vk : c == '\r' ? (ushort)0x0D : char.ToUpperInvariant(c),
            UnicodeChar = c,
            ControlKeyState = state,
        },
    };

    private static List<WindowsConsole.InputRecord> Typed(string text) =>
        text.SelectMany(c => new[] { Key(c), Key(c, down: false) }).ToList();

    [Fact]
    public void A_conhost_paste_burst_becomes_one_paste_with_its_newline()
    {
        var burst = Typed("alpha\rbravo");

        Assert.True(PasteBurst.Starts(burst.ToArray()));
        Assert.Equal("alpha\rbravo", PasteBurst.Paste(burst));
    }

    [Fact]
    public void A_paste_split_across_reads_is_joined_by_the_caller_and_still_one_paste()
    {
        var first = Typed("alpha");
        var second = Typed("\rbravo");

        Assert.True(PasteBurst.Starts(first.ToArray()));
        Assert.Equal("alpha\rbravo", PasteBurst.Paste([.. first, .. second]));
    }

    [Fact]
    public void One_typed_key_is_not_a_paste()
    {
        Assert.False(PasteBurst.Starts(Typed("a").ToArray()));
    }

    [Fact]
    public void A_short_burst_without_a_newline_stays_keys_so_dd_in_nvim_still_deletes()
    {
        var burst = Typed("dd");

        Assert.True(PasteBurst.Starts(burst.ToArray()));
        Assert.Null(PasteBurst.Paste(burst));
    }

    [Fact]
    public void A_short_burst_ending_in_enter_stays_keys_so_y_enter_still_submits()
    {
        Assert.Null(PasteBurst.Paste(Typed("y\r")));
        Assert.Null(PasteBurst.Paste(Typed("ls -la\r")));
        Assert.Equal("a\rb", PasteBurst.Paste(Typed("a\rb")));
    }

    [Fact]
    public void A_long_single_line_burst_is_a_paste()
    {
        Assert.Equal("git status --short", PasteBurst.Paste(Typed("git status --short")));
    }

    [Fact]
    public void A_chord_in_the_burst_means_it_was_typed_not_pasted()
    {
        var burst = Typed("ab");
        burst.Add(Key('\u0002', state: LeftCtrl, vk: 'B'));

        Assert.False(PasteBurst.Starts(burst.ToArray()));
        Assert.Null(PasteBurst.Paste([.. burst, .. Typed("\rmore text")]));
    }

    [Fact]
    public void AltGr_text_is_still_text()
    {
        var burst = Typed("user");
        burst.Add(Key('@', state: RightAlt | LeftCtrl, vk: 'Q'));
        burst.AddRange(Typed("host\rx"));

        Assert.Equal("user@host\rx", PasteBurst.Paste(burst));
    }

    [Fact]
    public void Crlf_and_lf_become_the_cr_a_terminal_sends()
    {
        Assert.Equal("a\rb\rc", PasteBurst.Paste(Typed("a\r\nb\nc")));
    }

    [Fact]
    public void Surrogate_pairs_survive()
    {
        Assert.Equal("hi 🙂\rok", PasteBurst.Paste(Typed("hi 🙂\rok")));
    }

    [Fact]
    public void A_paste_is_bracketed_only_for_a_pane_that_asked()
    {
        var paste = new TextMessage { Text = "alpha\rbravo", Paste = true };

        Assert.Equal("\e[200~alpha\rbravo\e[201~", FleetDaemon.PasteBytes(paste, bracketed: true));
        Assert.Equal("alpha\rbravo", FleetDaemon.PasteBytes(paste, bracketed: false));
    }

    [Fact]
    public void Pasted_text_cannot_close_the_bracket_early()
    {
        var paste = new TextMessage { Text = "safe\e[201~rm -rf .\r", Paste = true };

        Assert.Equal("\e[200~safe" + "rm -rf .\r\e[201~", FleetDaemon.PasteBytes(paste, bracketed: true));
    }

    [Fact]
    public void Typed_text_is_never_bracketed()
    {
        var typed = new TextMessage { Text = "x", Paste = false };

        Assert.Equal("x", FleetDaemon.PasteBytes(typed, bracketed: true));
    }
}
