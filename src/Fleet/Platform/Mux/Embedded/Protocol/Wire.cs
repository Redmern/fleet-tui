using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Mux.Embedded.Protocol;

public sealed class Wire(Stream stream) : IDisposable
{
    public const int Version = 1;

    public const int OldestVersion = 1;

    public static int? Agree(int peerOldest, int? peerHighest)
    {
        var agreed = Math.Min(peerHighest ?? peerOldest, Version);

        return agreed >= Math.Max(peerOldest, OldestVersion) ? agreed : null;
    }

    public static string? Refusal(Welcome welcome) =>
        Agree(welcome.Version, welcome.Version) is null ? Mismatch(welcome.Version, null, welcome.Build) : null;

    public static string Mismatch(int theirOldest, int? theirHighest, string? theirBuild) =>
        $"fleetd and this fleet cannot talk: one speaks protocol {Range(OldestVersion, Version)} (build {FleetVersion.Current}), "
        + $"the other {Range(theirOldest, theirHighest ?? theirOldest)} (build {theirBuild ?? "older"}). "
        + "Restart fleetd onto this build when convenient: fleet daemon stop, then fleet attach.";

    private static string Range(int oldest, int highest) => oldest == highest ? $"{oldest}" : $"{oldest}-{highest}";

    public const int MaxMessage = 16 * 1024 * 1024;

    public static readonly TimeSpan FinishSendingWithin = TimeSpan.FromSeconds(1);

    private readonly Lock _lanes = new();
    private readonly LinkedList<TaskCompletionSource> _urgent = new();
    private readonly LinkedList<TaskCompletionSource> _background = new();
    private bool _writing;
    private readonly byte[] _header = new byte[5];

    public Stream Stream => stream;

    public Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default) =>
        WriteAsync(type, payload, background: false, ct);

    public Task SendInBackgroundAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default) =>
        WriteAsync(type, payload, background: true, ct);

    private async Task WriteAsync(MessageType type, ReadOnlyMemory<byte> payload, bool background, CancellationToken ct)
    {
        var header = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(payload.Length + 1));
        header[4] = (byte)type;

        await EnterAsync(background, ct).ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(header, ct).ConfigureAwait(false);
            if (!payload.IsEmpty)
            {
                await stream.WriteAsync(payload, ct).ConfigureAwait(false);
            }

            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            Leave();
        }
    }

    private async Task EnterAsync(bool background, CancellationToken ct)
    {
        TaskCompletionSource turn;
        LinkedListNode<TaskCompletionSource> place;
        lock (_lanes)
        {
            ct.ThrowIfCancellationRequested();
            if (!_writing)
            {
                _writing = true;
                return;
            }

            turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            place = (background ? _background : _urgent).AddLast(turn);
        }

        try
        {
            await turn.Task.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!GiveUp(place))
            {
                Leave();
            }

            throw;
        }
    }

    private bool TryEnter(TimeSpan within)
    {
        TaskCompletionSource turn;
        LinkedListNode<TaskCompletionSource> place;
        lock (_lanes)
        {
            if (!_writing)
            {
                _writing = true;
                return true;
            }

            turn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            place = _urgent.AddLast(turn);
        }

        return turn.Task.Wait(within) || !GiveUp(place);
    }

    private bool GiveUp(LinkedListNode<TaskCompletionSource> place)
    {
        lock (_lanes)
        {
            if (place.List is not { } lane)
            {
                return false;
            }

            lane.Remove(place);
            return true;
        }
    }

    private void Leave()
    {
        TaskCompletionSource next;
        lock (_lanes)
        {
            var lane = _urgent.Count > 0 ? _urgent : _background;
            if (lane.First is not { } first)
            {
                _writing = false;
                return;
            }

            lane.RemoveFirst();
            next = first.Value;
        }

        next.SetResult();
    }

    public Task SendAsync<T>(MessageType type, T message, JsonTypeInfo<T> info, CancellationToken ct = default) =>
        SendAsync(type, JsonSerializer.SerializeToUtf8Bytes(message, info), ct);

    public Task SendFrameAsync(long seq, bool full, string bytes, CancellationToken ct = default)
    {
        var text = Encoding.UTF8.GetBytes(bytes);
        var payload = new byte[9 + text.Length];
        BinaryPrimitives.WriteInt64LittleEndian(payload, seq);
        payload[8] = full ? (byte)1 : (byte)0;
        text.CopyTo(payload, 9);
        return SendAsync(MessageType.Frame, payload, ct);
    }

    public async Task<(MessageType Type, byte[] Payload)?> ReceiveAsync(CancellationToken ct = default)
    {
        if (!await FillAsync(_header, ct).ConfigureAwait(false))
        {
            return null;
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(_header);
        if (length < 1 || length > MaxMessage)
        {
            throw new StrayBytesException(length, [.. _header]);
        }

        var payload = new byte[length - 1];
        if (!await FillAsync(payload, ct).ConfigureAwait(false))
        {
            return null;
        }

        return ((MessageType)_header[4], payload);
    }

    public static T Read<T>(byte[] payload, JsonTypeInfo<T> info) =>
        JsonSerializer.Deserialize(payload, info)
        ?? throw new InvalidDataException($"empty {typeof(T).Name}");

    public static (long Seq, bool Full, byte[] Bytes) ReadFrame(byte[] payload) =>
        (BinaryPrimitives.ReadInt64LittleEndian(payload), payload[8] == 1, payload[9..]);

    public void Dispose()
    {
        var sendFinished = TryEnter(FinishSendingWithin);
        try
        {
            stream.Dispose();
        }
        finally
        {
            if (sendFinished)
            {
                Leave();
            }
        }
    }

    private async Task<bool> FillAsync(byte[] buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0)
            {
                if (read == 0 && buffer == _header)
                {
                    return false;
                }

                throw new EndOfStreamException("the other side closed mid-message");
            }

            read += n;
        }

        return true;
    }
}
