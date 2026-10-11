using Fleet.Platform.Mux.Embedded.Client;
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

public class BracketedPasteHostTests
{
    private static byte[] B(string s) => System.Text.Encoding.UTF8.GetBytes(s);

    private static string Show(IEnumerable<PasteChunk> chunks) =>
        string.Join("|", chunks.Select(c => (c.Pasted ? "P:" : "T:") + System.Text.Encoding.UTF8.GetString(c.Bytes)));

    [Fact]
    public void Entering_the_host_on_unix_enables_bracketed_paste()
    {
        Assert.Contains("\e[?2004h", AttachClient.EnterSequence(windows: false, mouse: true), StringComparison.Ordinal);
        Assert.Contains("\e[?2004h", AttachClient.EnterSequence(windows: false, mouse: false), StringComparison.Ordinal);
    }

    [Fact]
    public void Entering_the_host_on_windows_keeps_the_paste_burst_and_sends_no_bracketed_paste()
    {
        Assert.DoesNotContain("\e[?2004h", AttachClient.EnterSequence(windows: true, mouse: true), StringComparison.Ordinal);
    }

    [Fact]
    public void Restoring_the_host_disables_bracketed_paste()
    {
        Assert.Contains("\e[?2004l", AttachClient.RestoreSequence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bracketed_paste_becomes_one_paste_chunk_between_typed_bytes()
    {
        var markers = new PasteMarkers();

        Assert.Equal("T:a|P:x\ry|T:b", Show(markers.Feed(B("a\e[200~x\ry\e[201~b"))));
    }

    [Fact]
    public void Typed_bytes_without_markers_pass_through_unchanged()
    {
        var markers = new PasteMarkers();

        Assert.Equal("T:ls\r", Show(markers.Feed(B("ls\r"))));
        Assert.Equal("T:\e", Show(markers.Feed(B("\e"))));
        Assert.Equal("T:\e[", Show(markers.Feed(B("\e["))));
        Assert.Equal("T:\e[A\e[<0;5;7M", Show(markers.Feed(B("\e[A\e[<0;5;7M"))));
    }

    [Fact]
    public void A_paste_split_across_reads_is_still_one_paste()
    {
        var markers = new PasteMarkers();

        Assert.Equal("T:a", Show(markers.Feed(B("a\e[200~hel"))));
        Assert.Equal(string.Empty, Show(markers.Feed(B("lo wor"))));
        Assert.Equal("P:hello world|T:z", Show(markers.Feed(B("ld\e[201~z"))));
    }

    [Fact]
    public void Markers_split_inside_themselves_across_reads_are_still_found()
    {
        var markers = new PasteMarkers();

        Assert.Equal(string.Empty, Show(markers.Feed(B("\e[20"))));
        Assert.Equal(string.Empty, Show(markers.Feed(B("0~one\e"))));
        Assert.Equal(string.Empty, Show(markers.Feed(B("[201"))));
        Assert.Equal("P:one|T:x", Show(markers.Feed(B("~x"))));
    }

    [Fact]
    public void A_held_partial_start_that_is_not_a_marker_is_typed_with_the_next_read()
    {
        var markers = new PasteMarkers();

        Assert.Equal(string.Empty, Show(markers.Feed(B("\e[2"))));
        Assert.Equal("T:\e[2~", Show(markers.Feed(B("~"))));
    }

    [Fact]
    public void A_mouse_sequence_inside_a_paste_stays_pasted_text()
    {
        var markers = new PasteMarkers();

        Assert.Equal("P:\e[<0;5;7M", Show(markers.Feed(B("\e[200~\e[<0;5;7M\e[201~"))));
    }

    [Fact]
    public void Multibyte_text_split_across_reads_survives()
    {
        var markers = new PasteMarkers();
        var smile = B("🙂");

        Assert.Empty(markers.Feed([.. B("\e[200~"), .. smile[..2]]));
        Assert.Equal("P:🙂", Show(markers.Feed([.. smile[2..], .. B("\e[201~")])));
    }

    [Fact]
    public void An_empty_paste_sends_nothing()
    {
        Assert.Empty(new PasteMarkers().Feed(B("\e[200~\e[201~")));
    }

    [Fact]
    public void A_pasted_chunk_reaches_a_pane_without_bracketed_paste_as_plain_text()
    {
        var chunk = new PasteMarkers().Feed(B("\e[200~alpha\rbravo\e[201~")).Single();
        var paste = new TextMessage { Text = System.Text.Encoding.UTF8.GetString(chunk.Bytes), Paste = chunk.Pasted };

        Assert.Equal("alpha\rbravo", FleetDaemon.PasteBytes(paste, bracketed: false));
        Assert.Equal("\e[200~alpha\rbravo\e[201~", FleetDaemon.PasteBytes(paste, bracketed: true));
    }
}
