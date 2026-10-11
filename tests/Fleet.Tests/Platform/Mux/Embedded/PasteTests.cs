using System.Text;
using System.Threading.Channels;
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
    private static readonly TimeSpan T0 = TimeSpan.FromSeconds(10);

    private static byte[] B(string s) => Encoding.UTF8.GetBytes(s);

    private static string Show(IEnumerable<PasteChunk> chunks) =>
        string.Join("|", chunks.Select(c => (c.Pasted ? "P:" : "T:") + Encoding.UTF8.GetString(c.Bytes)));

    private static string Feed(PasteMarkers markers, string text, TimeSpan? at = null) => Show(markers.Feed(B(text), at ?? T0));

    [Fact]
    public void Entering_the_host_on_unix_enables_bracketed_paste_after_the_existing_modes()
    {
        Assert.Equal("\e[22;0t\e[?1049h\e[H\e[2J" + SgrMouse.Enable + ModifiedKeys.Enable + "\e[?2004h",
            AttachClient.EnterSequence(windows: false, mouse: true));
        Assert.Equal("\e[22;0t\e[?1049h\e[H\e[2J" + ModifiedKeys.Enable + "\e[?2004h",
            AttachClient.EnterSequence(windows: false, mouse: false));
    }

    [Fact]
    public void Entering_the_host_on_windows_sends_only_the_alternate_screen_so_the_paste_burst_stays_in_charge()
    {
        Assert.Equal("\e[?1049h\e[H\e[2J", AttachClient.EnterSequence(windows: true, mouse: true));
    }

    [Fact]
    public void Restoring_the_host_disables_bracketed_paste()
    {
        Assert.Contains("\e[?2004l", AttachClient.RestoreSequence, StringComparison.Ordinal);
    }

    [Fact]
    public void A_bracketed_paste_becomes_one_paste_chunk_between_typed_bytes()
    {
        Assert.Equal("T:a|P:x\ry|T:b", Feed(new PasteMarkers(), "a\e[200~x\ry\e[201~b"));
    }

    [Fact]
    public void Typed_bytes_without_markers_pass_through_unchanged()
    {
        var markers = new PasteMarkers();

        Assert.Equal("T:ls\r", Feed(markers, "ls\r"));
        Assert.Equal("T:\e[A\e[<0;5;7M", Feed(markers, "\e[A\e[<0;5;7M"));
        Assert.Equal("T:\e[2~", Feed(markers, "\e[2~"));
        Assert.Null(markers.Deadline);
    }

    [Fact]
    public void A_paste_split_across_reads_is_still_one_paste()
    {
        var markers = new PasteMarkers();

        Assert.Equal("T:a", Feed(markers, "a\e[200~hel"));
        Assert.Equal(string.Empty, Feed(markers, "lo wor"));
        Assert.Equal("P:hello world|T:z", Feed(markers, "ld\e[201~z"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void A_start_marker_split_at_any_position_is_still_found(int at)
    {
        var markers = new PasteMarkers();
        const string start = "\e[200~";

        Assert.Equal(string.Empty, Feed(markers, start[..at]));
        Assert.Equal(T0 + PasteMarkers.StartHold, markers.Deadline);
        Assert.Equal(string.Empty, Feed(markers, start[at..] + "one"));
        Assert.Equal("P:one", Feed(markers, "\e[201~"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void An_end_marker_split_at_any_position_is_still_found(int at)
    {
        var markers = new PasteMarkers();
        const string end = "\e[201~";

        Assert.Equal(string.Empty, Feed(markers, "\e[200~one" + end[..at]));
        Assert.Equal("P:one|T:x", Feed(markers, end[at..] + "x"));
    }

    [Fact]
    public void A_lone_escape_is_held_only_until_the_start_hold_runs_out()
    {
        var markers = new PasteMarkers();

        Assert.Equal(string.Empty, Feed(markers, "\e"));
        Assert.Empty(markers.Expire(T0 + PasteMarkers.StartHold - TimeSpan.FromMilliseconds(1)));
        Assert.Equal("T:\e", Show(markers.Expire(T0 + PasteMarkers.StartHold)));
        Assert.Null(markers.Deadline);
    }

    [Fact]
    public void A_held_partial_start_that_is_not_a_marker_is_typed_with_the_next_read()
    {
        var markers = new PasteMarkers();

        Assert.Equal(string.Empty, Feed(markers, "\e[2"));
        Assert.Equal("T:\e[2~", Feed(markers, "~"));
    }

    [Fact]
    public void A_paste_whose_end_marker_never_comes_is_flushed_after_it_goes_quiet()
    {
        var markers = new PasteMarkers();

        Assert.Equal(string.Empty, Feed(markers, "\e[200~half a paste\e[20"));
        Assert.Equal(T0 + PasteMarkers.PasteIdle, markers.Deadline);
        Assert.Empty(markers.Expire(T0 + PasteMarkers.PasteIdle - TimeSpan.FromMilliseconds(1)));
        Assert.Equal("P:half a paste\e[20", Show(markers.Expire(T0 + PasteMarkers.PasteIdle)));
        Assert.Equal("T:typed again", Feed(markers, "typed again", T0 + TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void A_paste_keeps_its_idle_clock_running_from_the_last_read()
    {
        var markers = new PasteMarkers();

        Feed(markers, "\e[200~a", T0);
        Feed(markers, "b", T0 + TimeSpan.FromSeconds(1.5));

        Assert.Empty(markers.Expire(T0 + PasteMarkers.PasteIdle));
        Assert.Equal("P:ab", Show(markers.Expire(T0 + TimeSpan.FromSeconds(1.5) + PasteMarkers.PasteIdle)));
    }

    [Fact]
    public void A_paste_over_the_cap_is_flushed_and_the_rest_is_typed_without_the_late_end_marker()
    {
        var markers = new PasteMarkers(maxPaste: 4);

        Assert.Equal("P:abcd|T:ef", Feed(markers, "\e[200~abcdef\e[201~"));
        Assert.Null(markers.Deadline);
    }

    [Fact]
    public void A_paste_reaching_the_cap_across_reads_is_flushed_at_the_cap()
    {
        var markers = new PasteMarkers(maxPaste: 4);

        Assert.Equal(string.Empty, Feed(markers, "\e[200~ab"));
        Assert.Equal("P:abcd|T:ef", Feed(markers, "cdef"));
        Assert.Equal("T:g", Feed(markers, "\e[201~g"));
    }

    [Fact]
    public void The_cap_leaves_room_for_the_wire_after_json_escaping()
    {
        Assert.True(PasteMarkers.MaxPaste * 6 < Wire.MaxMessage);
    }

    [Fact]
    public void A_mouse_sequence_inside_a_paste_stays_pasted_text()
    {
        Assert.Equal("P:\e[<0;5;7M", Feed(new PasteMarkers(), "\e[200~\e[<0;5;7M\e[201~"));
    }

    [Fact]
    public void Multibyte_text_split_across_reads_survives()
    {
        var markers = new PasteMarkers();
        var smile = B("🙂");

        Assert.Empty(markers.Feed([.. B("\e[200~"), .. smile[..2]], T0));
        Assert.Equal("P:🙂", Show(markers.Feed([.. smile[2..], .. B("\e[201~")], T0)));
    }

    [Fact]
    public void An_empty_paste_sends_nothing()
    {
        Assert.Empty(new PasteMarkers().Feed(B("\e[200~\e[201~"), T0));
    }

    [Fact]
    public void A_pasted_chunk_reaches_a_pane_without_bracketed_paste_as_plain_text()
    {
        var chunk = new PasteMarkers().Feed(B("\e[200~alpha\rbravo\e[201~"), T0).Single();
        var paste = new TextMessage { Text = Encoding.UTF8.GetString(chunk.Bytes), Paste = chunk.Pasted };

        Assert.Equal("alpha\rbravo", FleetDaemon.PasteBytes(paste, bracketed: false));
        Assert.Equal("\e[200~alpha\rbravo\e[201~", FleetDaemon.PasteBytes(paste, bracketed: true));
    }

    private static byte[] Prefix => MuxKeys.Defaults.Prefix.Bytes()!;

    private static async Task<List<(MessageType Type, byte[] Payload)>> Stdin(Func<ChannelWriter<byte[]>, Task> type)
    {
        using var sent = new MemoryStream();
        var wire = new Wire(sent);
        var client = new AttachClient(Stream.Null, null, () => MuxKeys.Defaults, _ => { });
        var reads = Channel.CreateUnbounded<byte[]>();

        var loop = client.StdinAsync(wire, reads.Reader);
        await type(reads.Writer);
        reads.Writer.Complete();
        await loop.WaitAsync(TimeSpan.FromSeconds(10));

        sent.Position = 0;
        var back = new Wire(sent);
        var messages = new List<(MessageType, byte[])>();
        while (await back.ReceiveAsync() is { } message)
        {
            messages.Add(message);
        }

        return messages;
    }

    private static Task<List<(MessageType Type, byte[] Payload)>> Stdin(params byte[][] chunks) =>
        Stdin(writer =>
        {
            foreach (var chunk in chunks)
            {
                writer.TryWrite(chunk);
            }

            return Task.CompletedTask;
        });

    private static List<TextMessage> Texts(IEnumerable<(MessageType Type, byte[] Payload)> messages) =>
        messages.Where(m => m.Type == MessageType.Text).Select(m => Wire.Read(m.Payload, WireJsonContext.Default.TextMessage)).ToList();

    private static string Typed(TextMessage text) => Encoding.UTF8.GetString(Convert.FromBase64String(text.Bytes!));

    [Fact]
    public async Task Stdin_with_markers_is_sent_as_a_paste_and_a_mouse_sequence_inside_it_is_not_a_click()
    {
        var messages = await Stdin(B("a\e[200~x\e[<0;5;7My\ri"), B("s\e[201~\e[<0;2;3M"));

        var texts = Texts(messages);
        Assert.Equal(2, texts.Count);
        Assert.Equal("a", Typed(texts[0]));
        Assert.True(texts[1].Paste);
        Assert.Equal("x\e[<0;5;7My\ris", texts[1].Text);
        Assert.Single(messages, m => m.Type == MessageType.Mouse);
        Assert.Equal(MessageType.Mouse, messages[^1].Type);
    }

    [Fact]
    public async Task A_lone_escape_on_stdin_goes_out_on_its_own_without_waiting_for_the_next_key()
    {
        var messages = await Stdin(async writer =>
        {
            writer.TryWrite(B("\e"));
            await Task.Delay(PasteMarkers.StartHold * 5);
            writer.TryWrite(B("x"));
        });

        Assert.Equal(["\e", "x"], Texts(messages).Select(Typed));
    }

    [Fact]
    public async Task A_paste_while_the_prefix_is_armed_disarms_it_and_reaches_the_pane_as_text()
    {
        var messages = await Stdin(Prefix, B("\e[200~xq\e[201~"), B("x"));

        Assert.DoesNotContain(messages, m => m.Type == MessageType.Command);
        var texts = Texts(messages);
        Assert.True(texts[0].Paste);
        Assert.Equal("xq", texts[0].Text);
        Assert.Equal("x", Typed(texts[1]));
    }

    [Fact]
    public async Task A_paste_in_the_kill_pane_confirm_is_dropped_so_a_pasted_y_cannot_close_the_pane()
    {
        var messages = await Stdin([.. Prefix, .. B("x")], B("\e[200~y\e[201~"));

        Assert.DoesNotContain(messages, m => m.Type == MessageType.Command);
        Assert.Empty(Texts(messages));
    }
}
