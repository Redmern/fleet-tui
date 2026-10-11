using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;

namespace Fleet.Platform.Clipboard;

public static class DibPng
{
    private const int InfoHeader = 40;
    private const int MasksInHeader = 52;
    private const int AlphaMaskInHeader = 56;
    private const uint Rgb = 0;
    private const uint BitFields = 3;

    private static readonly byte[] Signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10];

    private static readonly uint[] CrcTable = BuildCrcTable();

    private readonly record struct Layout(int Width, int Height, bool TopDown, int Bpp, int Stride, int Pixels, uint R, uint G, uint B, uint A);

    public static byte[]? Convert(ReadOnlySpan<byte> dib)
    {
        if (Read(dib) is not { } layout)
        {
            return null;
        }

        var keepAlpha = layout.A != 0 && HasAnyAlpha(dib, layout);
        using var png = new MemoryStream();
        png.Write(Signature);

        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, layout.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), layout.Height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(png, "IHDR", ihdr);

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + (layout.Width * 4)];
            for (var y = 0; y < layout.Height; y++)
            {
                var source = layout.TopDown ? y : layout.Height - 1 - y;
                FillRow(dib.Slice(layout.Pixels + (source * layout.Stride), layout.Stride), layout, keepAlpha, row.AsSpan(1));
                z.Write(row);
            }
        }

        WriteChunk(png, "IDAT", compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static Layout? Read(ReadOnlySpan<byte> dib)
    {
        if (dib.Length < InfoHeader)
        {
            return null;
        }

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(dib);
        var width = BinaryPrimitives.ReadInt32LittleEndian(dib[4..]);
        var height = BinaryPrimitives.ReadInt32LittleEndian(dib[8..]);
        var bpp = BinaryPrimitives.ReadUInt16LittleEndian(dib[14..]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(dib[16..]);
        var colors = BinaryPrimitives.ReadUInt32LittleEndian(dib[32..]);
        if (headerSize < InfoHeader || headerSize > dib.Length || width <= 0 || height is 0 or int.MinValue
            || bpp is not (24 or 32) || colors > 256 || !(compression == Rgb || (compression == BitFields && bpp == 32)))
        {
            return null;
        }

        long pixels = headerSize + (colors * 4);
        uint r = 0x00FF0000, g = 0x0000FF00, b = 0x000000FF, a = bpp == 32 ? 0xFF000000 : 0;
        if (compression == BitFields)
        {
            var masksAt = headerSize >= MasksInHeader ? InfoHeader : headerSize;
            if (headerSize < MasksInHeader)
            {
                pixels += 12;
            }

            if (masksAt + 12 > dib.Length)
            {
                return null;
            }

            r = BinaryPrimitives.ReadUInt32LittleEndian(dib[masksAt..]);
            g = BinaryPrimitives.ReadUInt32LittleEndian(dib[(masksAt + 4)..]);
            b = BinaryPrimitives.ReadUInt32LittleEndian(dib[(masksAt + 8)..]);
            a = headerSize >= AlphaMaskInHeader ? BinaryPrimitives.ReadUInt32LittleEndian(dib[MasksInHeader..]) : 0;
        }

        var rows = Math.Abs((long)height);
        var stride = ((((long)width * bpp) + 31) / 32) * 4;
        if (pixels + (stride * rows) > dib.Length || (long)width * 4 >= Array.MaxLength)
        {
            return null;
        }

        return new Layout(width, (int)rows, height < 0, bpp, (int)stride, (int)pixels, r, g, b, a);
    }

    private static bool HasAnyAlpha(ReadOnlySpan<byte> dib, Layout layout)
    {
        for (var y = 0; y < layout.Height; y++)
        {
            var row = dib.Slice(layout.Pixels + (y * layout.Stride), layout.Stride);
            for (var x = 0; x < layout.Width; x++)
            {
                if ((BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]) & layout.A) != 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static void FillRow(ReadOnlySpan<byte> source, Layout layout, bool keepAlpha, Span<byte> rgba)
    {
        for (var x = 0; x < layout.Width; x++)
        {
            var target = rgba.Slice(x * 4, 4);
            if (layout.Bpp == 24)
            {
                target[0] = source[(x * 3) + 2];
                target[1] = source[(x * 3) + 1];
                target[2] = source[x * 3];
                target[3] = 0xFF;
                continue;
            }

            var pixel = BinaryPrimitives.ReadUInt32LittleEndian(source[(x * 4)..]);
            target[0] = Channel(pixel, layout.R);
            target[1] = Channel(pixel, layout.G);
            target[2] = Channel(pixel, layout.B);
            target[3] = keepAlpha ? Channel(pixel, layout.A) : (byte)0xFF;
        }
    }

    private static byte Channel(uint pixel, uint mask)
    {
        if (mask == 0)
        {
            return 0;
        }

        var shift = BitOperations.TrailingZeroCount(mask);
        var max = mask >> shift;
        var value = (pixel & mask) >> shift;
        return max == 0xFF ? (byte)value : (byte)(value * 255 / max);
    }

    private static void WriteChunk(Stream png, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        png.Write(word);

        Span<byte> name = stackalloc byte[4];
        System.Text.Encoding.ASCII.GetBytes(type, name);
        png.Write(name);
        png.Write(data);

        var crc = Update(Update(0xFFFFFFFFu, name), data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        png.Write(word);
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
