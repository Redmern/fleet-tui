using System.Buffers.Binary;
using Fleet.Platform.Clipboard;

namespace Fleet.Tests.Platform.Clipboard;

public class WindowsClipboardImageSourceTests
{
    private const int Max = 1024;
    private const uint PngFormat = 0xC123;

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 5];

    [Fact]
    public async Task The_registered_png_format_is_read_first_and_returned_as_is()
    {
        var clipboard = new FakeClipboard { [PngFormat] = Png, [WindowsClipboardImageSource.DibV5] = Dib(1, 1) };

        var image = await new WindowsClipboardImageSource(Max, clipboard).ReadPngAsync();

        Assert.Equal(Png, image.Png);
        Assert.Equal([(PngFormat, Max + 1)], clipboard.Reads);
    }

    [Fact]
    public async Task An_oversized_png_is_read_only_to_one_byte_past_the_limit()
    {
        var clipboard = new FakeClipboard { [PngFormat] = new byte[Max + 50] };

        var image = await new WindowsClipboardImageSource(Max, clipboard).ReadPngAsync();

        Assert.Equal(Max + 1, image.Png!.Length);
    }

    [Theory]
    [InlineData(WindowsClipboardImageSource.DibV5)]
    [InlineData(WindowsClipboardImageSource.Dib)]
    public async Task Without_a_png_format_a_dib_is_converted_to_png(uint format)
    {
        var clipboard = new FakeClipboard { [format] = Dib(1, 1) };

        var image = await new WindowsClipboardImageSource(Max, clipboard).ReadPngAsync();

        Assert.Equal(DibPng.Convert(Dib(1, 1)), image.Png);
    }

    [Fact]
    public async Task A_dibv5_is_preferred_over_a_plain_dib()
    {
        var clipboard = new FakeClipboard { [WindowsClipboardImageSource.DibV5] = Dib(1, 1), [WindowsClipboardImageSource.Dib] = Dib(2, 1) };

        var image = await new WindowsClipboardImageSource(Max, clipboard).ReadPngAsync();

        Assert.Equal(DibPng.Convert(Dib(1, 1)), image.Png);
    }

    [Fact]
    public async Task A_converted_png_over_the_limit_is_cut_to_one_byte_past_it()
    {
        var clipboard = new FakeClipboard { [WindowsClipboardImageSource.Dib] = Dib(64, 64, noisy: true) };

        var image = await new WindowsClipboardImageSource(Max, clipboard, maxDibBytes: 1 << 20).ReadPngAsync();

        Assert.Equal(Max + 1, image.Png!.Length);
    }

    [Fact]
    public async Task A_dib_over_its_own_limit_is_refused_without_converting()
    {
        var dib = Dib(8, 8);
        var clipboard = new FakeClipboard { [WindowsClipboardImageSource.Dib] = dib };

        var image = await new WindowsClipboardImageSource(Max, clipboard, maxDibBytes: dib.Length - 1).ReadPngAsync();

        Assert.Null(image.Png);
        Assert.Contains("too large", image.Refused);
        Assert.Equal(dib.Length, clipboard.Reads[^1].Limit);
    }

    [Fact]
    public async Task Without_an_image_there_is_no_image_and_no_refusal()
    {
        var empty = await new WindowsClipboardImageSource(Max, new FakeClipboard()).ReadPngAsync();
        var broken = await new WindowsClipboardImageSource(Max, new FakeClipboard { [WindowsClipboardImageSource.Dib] = [1, 2, 3] }).ReadPngAsync();

        Assert.Null(empty.Png);
        Assert.Null(empty.Refused);
        Assert.Null(broken.Png);
        Assert.Null(broken.Refused);
    }

    [Theory]
    [InlineData(PngFormat, true)]
    [InlineData(WindowsClipboardImageSource.DibV5, true)]
    [InlineData(WindowsClipboardImageSource.Dib, true)]
    [InlineData(13u, false)]
    public async Task A_probe_checks_the_formats_and_never_reads_the_image(uint format, bool has)
    {
        var clipboard = new FakeClipboard { [format] = [1] };

        var probed = await new WindowsClipboardImageSource(Max, clipboard).ProbePngAsync();

        Assert.Equal(has, probed.HasPng);
        Assert.Null(probed.Png);
        Assert.Empty(clipboard.Reads);
    }

    private static byte[] Dib(int width, int height, bool noisy = false)
    {
        var stride = width * 4;
        var dib = new byte[40 + (stride * height)];
        BinaryPrimitives.WriteInt32LittleEndian(dib, 40);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(4), width);
        BinaryPrimitives.WriteInt32LittleEndian(dib.AsSpan(8), height);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(12), 1);
        BinaryPrimitives.WriteInt16LittleEndian(dib.AsSpan(14), 32);
        if (noisy)
        {
            new Random(7).NextBytes(dib.AsSpan(40));
        }

        return dib;
    }

    private sealed class FakeClipboard : Dictionary<uint, byte[]>, IWindowsClipboard
    {
        public List<(uint Format, int Limit)> Reads { get; } = [];

        public uint PngFormat => WindowsClipboardImageSourceTests.PngFormat;

        public bool Has(uint format) => ContainsKey(format);

        public byte[]? Read(uint format, int limit)
        {
            Reads.Add((format, limit));
            return TryGetValue(format, out var data) ? data[..Math.Min(limit, data.Length)] : null;
        }
    }
}
