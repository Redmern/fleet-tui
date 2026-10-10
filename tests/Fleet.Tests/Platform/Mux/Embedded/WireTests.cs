using Fleet.Platform.Mux.Embedded.Input;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class WireTests
{
    [Theory]
    [InlineData(1, null, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(1, 9, 1)]
    public void Peers_agree_on_the_highest_protocol_both_speak(int oldest, int? highest, int agreed) =>
        Assert.Equal(agreed, Wire.Agree(oldest, highest));

    [Theory]
    [InlineData(2, null)]
    [InlineData(2, 9)]
    [InlineData(999, null)]
    public void A_peer_whose_oldest_protocol_is_newer_than_ours_is_refused(int oldest, int? highest) =>
        Assert.Null(Wire.Agree(oldest, highest));

    [Fact]
    public void A_welcome_on_a_protocol_this_build_does_not_speak_is_refused_with_a_restart_hint()
    {
        var refused = Wire.Refusal(new Welcome { Version = 999, Build = "9.9.9" });

        Assert.NotNull(refused);
        Assert.Contains("9.9.9", refused);
        Assert.Contains("fleet daemon stop", refused);
        Assert.Null(Wire.Refusal(new Welcome { Version = Wire.Version }));
    }

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
    public async Task Closing_the_wire_lets_a_message_being_sent_finish_so_the_peer_never_gets_half_of_it()
    {
        var peer = new SlowPayload();
        var wire = new Wire(peer);
        var sending = wire.SendAsync(MessageType.Text, new byte[10]);
        await peer.PayloadStarted.Task;

        // Close on a thread of its own and finish the payload from this thread, inline:
        // nothing between "close starts waiting" and "send releases the gate" may wait
        // for the thread pool, which a busy CI runner can hold up past Dispose's 1 s budget.
        var closing = Task.Factory.StartNew(wire.Dispose, TaskCreationOptions.LongRunning);
        Thread.Sleep(200);
        peer.FinishPayload.SetResult();
        await sending;
        await closing;

        Assert.False(peer.ClosedMidMessage);
    }

    private sealed class SlowPayload : MemoryStream
    {
        private volatile bool _inPayload;

        public TaskCompletionSource PayloadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource FinishPayload { get; } = new();

        public bool ClosedMidMessage { get; private set; }

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Length >= 5)
            {
                _inPayload = true;
                PayloadStarted.SetResult();
                await FinishPayload.Task.ConfigureAwait(false);
                _inPayload = false;
            }

            await base.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            ClosedMidMessage |= _inPayload;
            base.Dispose(disposing);
        }
    }

    [Fact]
    public async Task A_key_sent_behind_background_requests_waits_for_at_most_the_write_in_progress()
    {
        var peer = new HeldFirstWrite();
        var wire = new Wire(peer);
        var sends = new List<Task> { wire.SendInBackgroundAsync(MessageType.Request, "a"u8.ToArray()) };
        await peer.Holding.Task;

        sends.Add(wire.SendInBackgroundAsync(MessageType.Request, "b"u8.ToArray()));
        sends.Add(wire.SendInBackgroundAsync(MessageType.Request, "c"u8.ToArray()));
        sends.Add(wire.SendAsync(MessageType.Key, "k"u8.ToArray()));
        sends.Add(wire.SendInBackgroundAsync(MessageType.Request, "d"u8.ToArray()));
        sends.Add(wire.SendAsync(MessageType.Frame, "f"u8.ToArray()));
        peer.Release.SetResult();
        await Task.WhenAll(sends);

        peer.Position = 0;
        var reader = new Wire(peer);
        var order = new List<string>();
        while (await reader.ReceiveAsync() is { } message)
        {
            order.Add($"{message.Type}:{System.Text.Encoding.ASCII.GetString(message.Payload)}");
        }

        Assert.Equal(["Request:a", "Key:k", "Frame:f", "Request:b", "Request:c", "Request:d"], order);
    }

    [Fact]
    public async Task A_background_send_cancelled_while_waiting_never_writes_and_lets_the_next_one_through()
    {
        var peer = new HeldFirstWrite();
        var wire = new Wire(peer);
        var first = wire.SendAsync(MessageType.Key, "a"u8.ToArray());
        await peer.Holding.Task;

        using var cancel = new CancellationTokenSource();
        var cancelled = wire.SendInBackgroundAsync(MessageType.Request, "x"u8.ToArray(), cancel.Token);
        var after = wire.SendInBackgroundAsync(MessageType.Request, "b"u8.ToArray());
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        peer.Release.SetResult();
        await Task.WhenAll(first, after);

        peer.Position = 0;
        var reader = new Wire(peer);
        Assert.Equal("a", System.Text.Encoding.ASCII.GetString((await reader.ReceiveAsync())!.Value.Payload));
        Assert.Equal("b", System.Text.Encoding.ASCII.GetString((await reader.ReceiveAsync())!.Value.Payload));
        Assert.Null(await reader.ReceiveAsync());
    }

    private sealed class HeldFirstWrite : MemoryStream
    {
        private int _writes;

        public TaskCompletionSource Holding { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _writes) == 1)
            {
                Holding.SetResult();
                await Release.Task;
            }

            await base.WriteAsync(buffer, cancellationToken);
        }
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
