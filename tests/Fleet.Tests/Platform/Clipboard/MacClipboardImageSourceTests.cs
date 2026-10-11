using System.Text;
using Fleet.Platform.Clipboard;

namespace Fleet.Tests.Platform.Clipboard;

public class MacClipboardImageSourceTests
{
    private const int Max = 64;

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 5];

    private static readonly (byte[]?, string?) ClipboardImageNone = (null, null);

    private static byte[] AppleScriptData(byte[] png) =>
        Encoding.UTF8.GetBytes($"«data PNGf{Convert.ToHexString(png)}»\n");

    [Fact]
    public async Task Pngpaste_is_asked_first_for_a_png()
    {
        var tools = new FakeTools { ["pngpaste"] = (0, Png), ["osascript"] = (0, AppleScriptData([1, 2])) };

        var image = await new MacClipboardImageSource(Max, tools.RunAsync).ReadPngAsync();

        Assert.Equal(Png, image.Png);
        Assert.Equal(["pngpaste -"], tools.Ran);
    }

    [Fact]
    public async Task Osascript_reads_the_png_when_pngpaste_is_not_installed()
    {
        var tools = new FakeTools { ["osascript"] = (0, AppleScriptData(Png)) };

        var image = await new MacClipboardImageSource(Max, tools.RunAsync).ReadPngAsync();

        Assert.Equal(Png, image.Png);
        Assert.Equal("osascript -e the clipboard as «class PNGf»", tools.Ran[^1]);
    }

    [Fact]
    public async Task Osascript_reads_the_png_when_pngpaste_finds_no_image()
    {
        var tools = new FakeTools { ["pngpaste"] = (1, []), ["osascript"] = (0, AppleScriptData(Png)) };

        Assert.Equal(Png, (await new MacClipboardImageSource(Max, tools.RunAsync).ReadPngAsync()).Png);
    }

    [Fact]
    public async Task Without_a_reader_or_an_image_there_is_no_image_and_no_refusal()
    {
        var none = await new MacClipboardImageSource(Max, new FakeTools().RunAsync).ReadPngAsync();
        var text = await new MacClipboardImageSource(Max, new FakeTools
        {
            ["pngpaste"] = (1, []),
            ["osascript"] = (1, "execution error: Can’t make some data into the expected type. (-1700)\n"u8.ToArray()),
        }.RunAsync).ReadPngAsync();
        var garbage = await new MacClipboardImageSource(Max, new FakeTools { ["osascript"] = (0, "hello\n"u8.ToArray()) }.RunAsync).ReadPngAsync();

        Assert.Equal(ClipboardImageNone, (none.Png, none.Refused));
        Assert.Equal(ClipboardImageNone, (text.Png, text.Refused));
        Assert.Equal(ClipboardImageNone, (garbage.Png, garbage.Refused));
    }

    [Fact]
    public async Task Pngpaste_stops_one_byte_past_the_limit()
    {
        var tools = new FakeTools { ["pngpaste"] = (0, new byte[Max + 10]) };

        var image = await new MacClipboardImageSource(Max, tools.RunAsync).ReadPngAsync();

        Assert.Equal([Max + 1], tools.Limits);
        Assert.Equal(Max + 1, image.Png!.Length);
    }

    [Fact]
    public async Task Osascript_output_of_an_oversized_image_is_cut_and_decoded_to_one_byte_past_the_limit()
    {
        var big = Enumerable.Range(0, Max + 40).Select(i => (byte)i).ToArray();
        var tools = new FakeTools { ["osascript"] = (0, AppleScriptData(big)) };

        var image = await new MacClipboardImageSource(Max, tools.RunAsync).ReadPngAsync();

        Assert.Equal(big[..(Max + 1)], image.Png);
        Assert.True(tools.Limits[^1] < AppleScriptData(big).Length);
    }

    [Theory]
    [InlineData("«class PNGf», 1234, «class 8BPS», 5678, TIFF picture, 9000\n", true)]
    [InlineData("«class utf8», 5, string, 5, Unicode text, 10\n", false)]
    public async Task A_probe_asks_osascript_for_the_clipboard_info_and_never_reads_the_image(string info, bool has)
    {
        var tools = new FakeTools { ["osascript"] = (0, Encoding.UTF8.GetBytes(info)) };

        var probed = await new MacClipboardImageSource(Max, tools.RunAsync).ProbePngAsync();

        Assert.Equal(has, probed.HasPng);
        Assert.Null(probed.Png);
        Assert.Equal(["osascript -e clipboard info"], tools.Ran);
    }

    [Fact]
    public async Task A_probe_without_osascript_finds_no_image()
    {
        var probed = await new MacClipboardImageSource(Max, new FakeTools().RunAsync).ProbePngAsync();

        Assert.False(probed.HasPng);
    }

    private sealed class FakeTools : Dictionary<string, (int Code, byte[] Stdout)>
    {
        public List<string> Ran { get; } = [];

        public List<int> Limits { get; } = [];

        public Task<ClipboardRun?> RunAsync(string program, IReadOnlyList<string> args, int limit, CancellationToken ct)
        {
            Ran.Add(string.Join(' ', [program, .. args]));
            Limits.Add(limit);
            return Task.FromResult(TryGetValue(program, out var result)
                ? new ClipboardRun(result.Code, result.Stdout.Length >= limit ? result.Stdout[..limit] : result.Stdout, result.Stdout.Length >= limit)
                : null);
        }
    }
}
