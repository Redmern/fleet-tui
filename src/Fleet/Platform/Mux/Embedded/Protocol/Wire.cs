using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Fleet.Platform.Mux.Embedded.Protocol;

public sealed class Wire(Stream stream) : IDisposable
{
    public const int Version = 1;

    public const int MaxMessage = 16 * 1024 * 1024;

    public static readonly TimeSpan FinishSendingWithin = TimeSpan.FromSeconds(1);

    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly byte[] _header = new byte[5];

    public Stream Stream => stream;

    public async Task SendAsync(MessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default)
    {
        var header = new byte[5];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)(payload.Length + 1));
        header[4] = (byte)type;

        await _writeGate.WaitAsync(ct).ConfigureAwait(false);
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
            _writeGate.Release();
        }
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
        var sendFinished = _writeGate.Wait(FinishSendingWithin);
        try
        {
            stream.Dispose();
        }
        finally
        {
            if (sendFinished)
            {
                _writeGate.Release();
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
