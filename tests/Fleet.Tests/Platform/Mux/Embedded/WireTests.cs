using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class WireTests
{
    [Fact]
    public async Task A_json_message_survives_the_round_trip()
    {
        var buffer = new MemoryStream();
        var writer = new Wire(buffer);

        await writer.SendAsync(
            MessageType.Request,
            new ControlRequest { Id = 7, Op = "spawn", Args = ["claude"], Workspace = "techweb" },
            WireJsonContext.Default.ControlRequest);

        buffer.Position = 0;
        var received = await new Wire(buffer).ReceiveAsync();

        Assert.NotNull(received);
        Assert.Equal(MessageType.Request, received.Value.Type);
        var request = Wire.Read(received.Value.Payload, WireJsonContext.Default.ControlRequest);
        Assert.Equal(7, request.Id);
        Assert.Equal("spawn", request.Op);
        Assert.Equal(["claude"], request.Args);
        Assert.Equal("techweb", request.Workspace);
    }

    [Fact]
    public async Task A_frame_keeps_its_sequence_flag_and_bytes()
    {
        var buffer = new MemoryStream();
        await new Wire(buffer).SendFrameAsync(42, full: true, "\e[2Jhé");

        buffer.Position = 0;
        var received = await new Wire(buffer).ReceiveAsync();
        var (seq, full, bytes) = Wire.ReadFrame(received!.Value.Payload);

        Assert.Equal(MessageType.Frame, received.Value.Type);
        Assert.Equal(42, seq);
        Assert.True(full);
        Assert.Equal("\e[2Jhé", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task A_message_without_a_payload_is_one_write_so_a_peer_that_hangs_up_on_reading_it_cannot_break_the_pipe()
    {
        var peer = new HangsUpAfterOneWrite();

        await new Wire(peer).SendAsync(MessageType.Bye, ReadOnlyMemory<byte>.Empty);

        peer.Position = 0;
        Assert.Equal(MessageType.Bye, (await new Wire(peer).ReceiveAsync())!.Value.Type);
    }

    private sealed class HangsUpAfterOneWrite : MemoryStream
    {
        private int _writes;

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
            ++_writes > 1
                ? throw new IOException("Broken pipe")
                : base.WriteAsync(buffer, cancellationToken);
    }

    [Fact]
    public async Task A_closed_stream_reads_as_the_end_not_an_error()
    {
        var received = await new Wire(new MemoryStream()).ReceiveAsync();

        Assert.Null(received);
    }

    [Fact]
    public async Task A_message_cut_off_mid_way_is_an_error()
    {
        var buffer = new MemoryStream();
        await new Wire(buffer).SendAsync(MessageType.Text, new byte[10]);
        var truncated = new MemoryStream(buffer.ToArray()[..8]);

        await Assert.ThrowsAsync<EndOfStreamException>(() => new Wire(truncated).ReceiveAsync());
    }

    [Fact]
    public async Task An_absurd_length_is_refused_before_allocating()
    {
        var bogus = new MemoryStream([0xff, 0xff, 0xff, 0x7f, 1]);

        await Assert.ThrowsAsync<StrayBytesException>(() => new Wire(bogus).ReceiveAsync());
    }

    [Fact]
    public async Task Text_where_a_message_should_be_keeps_its_first_bytes_so_the_client_can_show_it()
    {
        var text = new MemoryStream("fleet: not the protocol\n"u8.ToArray());

        var stray = await Assert.ThrowsAsync<StrayBytesException>(() => new Wire(text).ReceiveAsync());

        Assert.Equal("fleet", System.Text.Encoding.ASCII.GetString(stray.Header));
    }

    [Fact]
    public void Pane_modes_follow_the_requests_even_when_split_across_reads()
    {
        var modes = new ConPtyModes();

        modes.Feed("\e[?90"u8);
        modes.Feed("01h\e[?2004h"u8);

        Assert.True(modes.Win32Input);
        Assert.True(modes.BracketedPaste);

        modes.Feed("\e[?2004l"u8);
        Assert.False(modes.BracketedPaste);
    }

    [Fact]
    public void A_key_record_encodes_as_win32_input_mode()
    {
        var bytes = ConPtyModes.Encode(0x55, 22, 0x15, true, 0x8, 1);

        Assert.Equal("\e[85;22;21;1;8;1_", System.Text.Encoding.ASCII.GetString(bytes));
    }
}
