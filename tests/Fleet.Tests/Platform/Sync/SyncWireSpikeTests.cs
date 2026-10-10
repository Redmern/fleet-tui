using System.IO.Pipelines;
using System.Security.Cryptography;
using Fleet.Platform.Mux.Embedded.Protocol;

namespace Fleet.Tests.Platform.Sync;

// Phase 0 spike for host sync (docs/host-sync-wire.md): one small file framed on fleetd's Wire,
// relayed through a byte-for-byte copy the way `fleet bridge` relays ssh stdin to fleetd, and
// reassembled and verified on the far side. No fleetd change yet: this settles the framing only.
public sealed class SyncWireSpikeTests
{
    private const MessageType SyncData = (MessageType)40;
    private const int Chunk = 64 * 1024;

    [Fact]
    public async Task A_small_file_crosses_a_bridge_relay_intact_in_binary_frames()
    {
        var file = RandomNumberGenerator.GetBytes(200_000);
        var id = Guid.NewGuid();

        var toBridge = new Pipe();
        var toFleetd = new Pipe();

        var bridge = toBridge.Reader.AsStream().CopyToAsync(toFleetd.Writer.AsStream())
            .ContinueWith(_ => toFleetd.Writer.Complete(), TaskScheduler.Default);

        var receive = ReceiveAsync(new Wire(toFleetd.Reader.AsStream()));

        using (var sender = new Wire(toBridge.Writer.AsStream()))
        {
            await sender.SendAsync(MessageType.Request, Open(id, "notes.txt", file), WireJsonContext.Default.ControlRequest);

            for (var offset = 0; offset < file.Length; offset += Chunk)
            {
                var part = file.AsMemory(offset, Math.Min(Chunk, file.Length - offset));
                var payload = new byte[16 + part.Length];
                id.TryWriteBytes(payload);
                part.CopyTo(payload.AsMemory(16));
                await sender.SendAsync(SyncData, payload);
            }

            await sender.SendAsync(
                MessageType.Request,
                new ControlRequest { Op = "sync-close", Args = [id.ToString("N")] },
                WireJsonContext.Default.ControlRequest);
        }

        await toBridge.Writer.CompleteAsync();
        await bridge;

        var (name, expectedHash, received, frames) = await receive;

        Assert.Equal("notes.txt", name);
        Assert.Equal(file, received);
        Assert.Equal(expectedHash, Convert.ToHexStringLower(SHA256.HashData(received)));
        Assert.Equal((file.Length + Chunk - 1) / Chunk, frames);
    }

    [Fact]
    public void A_data_frame_with_its_id_fits_well_inside_the_wire_limit()
    {
        Assert.True(16 + Chunk < Wire.MaxMessage);
    }

    private static ControlRequest Open(Guid id, string name, byte[] file) => new()
    {
        Op = "sync-open",
        Args = [id.ToString("N"), name, file.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Convert.ToHexStringLower(SHA256.HashData(file))],
    };

    private static async Task<(string Name, string Hash, byte[] Bytes, int Frames)> ReceiveAsync(Wire wire)
    {
        using var buffer = new MemoryStream();
        string name = string.Empty, hash = string.Empty;
        Guid id = Guid.Empty;
        var frames = 0;

        while (await wire.ReceiveAsync() is { } message)
        {
            if (message.Type == MessageType.Request)
            {
                var request = Wire.Read(message.Payload, WireJsonContext.Default.ControlRequest);

                if (request.Op == "sync-open")
                {
                    id = Guid.ParseExact(request.Args![0], "N");
                    name = request.Args[1];
                    hash = request.Args[3];
                }
                else if (request.Op == "sync-close")
                {
                    break;
                }
            }
            else if (message.Type == SyncData && new Guid(message.Payload.AsSpan(0, 16)) == id)
            {
                buffer.Write(message.Payload, 16, message.Payload.Length - 16);
                frames++;
            }
        }

        return (name, hash, buffer.ToArray(), frames);
    }
}
