using System.Buffers.Binary;
using System.IO.Compression;
using Fleet.Platform.Clipboard;

namespace Fleet.Tests.Platform.Clipboard;

public class DibPngTests
{
    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10];

    [Fact]
    public void A_bottom_up_32_bit_dib_becomes_an_rgba_png_with_rows_flipped_upright()
    {
        var dib = Dib(width: 2, height: 2, bpp: 32, compression: 0, pixelRows:
        [
            [0x10, 0x20, 0x30, 0xFF, 0x11, 0x21, 0x31, 0x80],
            [0x40, 0x50, 0x60, 0xFF, 0x41, 0x51, 0x61, 0x7F],
        ]);

        var (width, height, rgba) = Decode(DibPng.Convert(dib)!);

        Assert.Equal((2, 2), (width, height));
        Assert.Equal(new byte[]
        {
            0x60, 0x50, 0x40, 0xFF, 0x61, 0x51, 0x41, 0x7F,
            0x30, 0x20, 0x10, 0xFF, 0x31, 0x21, 0x11, 0x80,
        }, rgba);
    }

    [Fact]
    public void A_32_bit_dib_whose_alpha_is_all_zero_is_treated_as_opaque()
    {
        var dib = Dib(width: 1, height: -1, bpp: 32, compression: 0, pixelRows: [[1, 2, 3, 0]]);

        Assert.Equal(new byte[] { 3, 2, 1, 0xFF }, Decode(DibPng.Convert(dib)!).Rgba);
    }

    [Fact]
    public void A_top_down_24_bit_dib_with_padded_rows_becomes_an_opaque_png()
    {
        var dib = Dib(width: 1, height: -2, bpp: 24, compression: 0, pixelRows:
        [
            [1, 2, 3, 0],
            [4, 5, 6, 0],
        ]);

        var (width, height, rgba) = Decode(DibPng.Convert(dib)!);

        Assert.Equal((1, 2), (width, height));
        Assert.Equal(new byte[] { 3, 2, 1, 0xFF, 6, 5, 4, 0xFF }, rgba);
    }

    [Fact]
    public void A_bitfields_dib_with_masks_after_a_plain_header_uses_those_masks()
    {
        var masks = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(masks, 0x000000FF);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(4), 0x0000FF00);
        BinaryPrimitives.WriteUInt32LittleEndian(masks.AsSpan(8), 0x00FF0000);
        var dib = Dib(width: 1, height: 1, bpp: 32, compression: 3, pixelRows: [[1, 2, 3, 9]], afterHeader: masks);

        Assert.Equal(new byte[] { 1, 2, 3, 0xFF }, Decode(DibPng.Convert(dib)!).Rgba);
    }

    [Fact]
    public void A_v5_bitfields_dib_keeps_its_alpha_mask()
    {
        var dib = Dib(width: 1, height: 1, bpp: 32, compression: 3, pixelRows: [[1, 2, 3, 0x40]], headerSize: 124,
            masks: (0x00FF0000, 0x0000FF00, 0x000000FF, 0xFF000000));

        Assert.Equal(new byte[] { 3, 2, 1, 0x40 }, Decode(DibPng.Convert(dib)!).Rgba);
    }

    [Fact]
    public void A_dib_the_converter_cannot_read_gives_nothing()
    {
        Assert.Null(DibPng.Convert([]));
        Assert.Null(DibPng.Convert(Dib(width: 1, height: 1, bpp: 8, compression: 0, pixelRows: [[0, 0, 0, 0]])));
        Assert.Null(DibPng.Convert(Dib(width: 1, height: 1, bpp: 32, compression: 1, pixelRows: [[0, 0, 0, 0]])));
        Assert.Null(DibPng.Convert(Dib(width: 0, height: 1, bpp: 32, compression: 0, pixelRows: [[]])));
        Assert.Null(DibPng.Convert(Dib(width: 4, height: 4, bpp: 32, compression: 0, pixelRows: [[0, 0, 0, 0]])));
        Assert.Null(DibPng.Convert(Dib(width: int.MaxValue, height: int.MaxValue, bpp: 32, compression: 0, pixelRows: [[0, 0, 0, 0]])));
    }

    [Fact]
    public void The_png_chunks_carry_valid_crcs()
    {
        var png = DibPng.Convert(Dib(width: 1, height: 1, bpp: 32, compression: 0, pixelRows: [[1, 2, 3, 4]]))!;

        foreach (var (type, data, crc) in Chunks(png))
        {
            Assert.Equal(Crc32([.. System.Text.Encoding.ASCII.GetBytes(type), .. data]), crc);
        }

        Assert.Equal(["IHDR", "IDAT", "IEND"], Chunks(png).Select(c => c.Type));
    }

    private static byte[] Dib(int width, int height, short bpp, uint compression, byte[][] pixelRows, int headerSize = 40,
        byte[]? afterHeader = null, (uint R, uint G, uint B, uint A)? masks = null)
    {
        var header = new byte[headerSize];
        BinaryPrimitives.WriteInt32LittleEndian(header, headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), height);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(14), bpp);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), compression);
        if (masks is { } m)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(40), m.R);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(44), m.G);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(48), m.B);
            BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(52), m.A);
        }

        return [.. header, .. afterHeader ?? [], .. pixelRows.SelectMany(r => r)];
    }

    private static (int Width, int Height, byte[] Rgba) Decode(byte[] png)
    {
        Assert.Equal(Signature, png[..8]);
        var chunks = Chunks(png).ToList();
        var ihdr = chunks.Single(c => c.Type == "IHDR").Data;
        var width = BinaryPrimitives.ReadInt32BigEndian(ihdr);
        var height = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4));
        Assert.Equal(new byte[] { 8, 6, 0, 0, 0 }, ihdr[8..]);

        using var inflated = new MemoryStream();
        using (var z = new ZLibStream(new MemoryStream([.. chunks.Where(c => c.Type == "IDAT").SelectMany(c => c.Data)]), CompressionMode.Decompress))
        {
            z.CopyTo(inflated);
        }

        var raw = inflated.ToArray();
        var rgba = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            var row = raw.AsSpan(y * ((width * 4) + 1), (width * 4) + 1);
            Assert.Equal(0, row[0]);
            rgba.AddRange(row[1..].ToArray());
        }

        return (width, height, rgba.ToArray());
    }

    private static IEnumerable<(string Type, byte[] Data, uint Crc)> Chunks(byte[] png)
    {
        var at = 8;
        while (at < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(at));
            var type = System.Text.Encoding.ASCII.GetString(png, at + 4, 4);
            var data = png[(at + 8)..(at + 8 + length)];
            var crc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at + 8 + length));
            yield return (type, data, crc);
            at += 12 + length;
        }
    }

    private static uint Crc32(byte[] bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}
