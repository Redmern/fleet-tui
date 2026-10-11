using System.Text;
using Fleet.Features.Shim.ServeClipboard;
using Fleet.Features.Shim.ServeClipboard.Enums;
using Fleet.Ports;

namespace Fleet.Tests.Features.Shim.ServeClipboard;

public class ClipboardShimTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 7];

    [Theory]
    [InlineData("xclip", "-selection clipboard -t image/png -o", ShimRead.Png)]
    [InlineData("xclip", "-sel clip -t image/png -out", ShimRead.Png)]
    [InlineData("xclip", "-o -selection c -target image/png", ShimRead.Png)]
    [InlineData("xclip", "-selection clipboard -t TARGETS -o", ShimRead.Types)]
    [InlineData("xclip", "-selection clipboard -t image/jpeg -o", ShimRead.OtherImage)]
    [InlineData("wl-paste", "--type image/png", ShimRead.Png)]
    [InlineData("wl-paste", "-t image/png", ShimRead.Png)]
    [InlineData("wl-paste", "--type=image/png --no-newline", ShimRead.Png)]
    [InlineData("wl-paste", "--list-types", ShimRead.Types)]
    [InlineData("wl-paste", "-l", ShimRead.Types)]
    [InlineData("wl-paste", "--type image/gif", ShimRead.OtherImage)]
    public void The_image_reads_claude_code_makes_are_served(string tool, string args, ShimRead expected) =>
        Assert.Equal(expected, ClipboardShim.Parse(tool, Split(args)).Read);

    [Theory]
    [InlineData("xclip", "-selection clipboard -o")]
    [InlineData("xclip", "-selection clipboard -t UTF8_STRING -o")]
    [InlineData("xclip", "-t image/png -o")]
    [InlineData("xclip", "-selection primary -t image/png -o")]
    [InlineData("xclip", "-selection clipboard -t image/png -i shot.png")]
    [InlineData("xclip", "-selection clipboard")]
    [InlineData("wl-paste", "")]
    [InlineData("wl-paste", "--type text/plain")]
    [InlineData("wl-paste", "--primary --type image/png")]
    [InlineData("wl-paste", "--watch cat")]
    [InlineData("pbpaste", "")]
    public void Every_other_call_goes_to_the_real_tool(string tool, string args) =>
        Assert.Equal(ShimRead.RealTool, ClipboardShim.Parse(tool, Split(args)).Read);

    [Theory]
    [InlineData("xclip", "-selection clipboard -t image/png -o")]
    [InlineData("wl-paste", "--type image/png")]
    public async Task A_png_on_the_viewer_clipboard_is_written_to_stdout_as_is(string tool, string args)
    {
        var (code, stdout, stderr) = await RunAsync(tool, args, new ClipboardImage(Png));

        Assert.Equal(0, code);
        Assert.Equal(Png, stdout);
        Assert.Empty(stderr);
    }

    [Theory]
    [InlineData("xclip", "-selection clipboard -t TARGETS -o", "TARGETS\nimage/png\n")]
    [InlineData("wl-paste", "--list-types", "image/png\n")]
    public async Task Listing_types_shows_png_when_the_viewer_has_an_image(string tool, string args, string listed)
    {
        var (code, stdout, _) = await RunAsync(tool, args, new ClipboardImage(Png));

        Assert.Equal(0, code);
        Assert.Equal(listed, Encoding.UTF8.GetString(stdout));
    }

    [Theory]
    [InlineData("xclip", "-selection clipboard -t image/png -o", "Error: target image/png not available")]
    [InlineData("xclip", "-selection clipboard -t TARGETS -o", "Error: target TARGETS not available")]
    [InlineData("xclip", "-selection clipboard -t image/jpeg -o", "Error: target image/jpeg not available")]
    [InlineData("wl-paste", "--type image/png", "Nothing is copied")]
    [InlineData("wl-paste", "--list-types", "Nothing is copied")]
    public async Task No_image_exits_like_the_native_tool_with_empty_output(string tool, string args, string native)
    {
        var (code, stdout, stderr) = await RunAsync(tool, args, ClipboardImage.None);

        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Equal(native, stderr.TrimEnd());
    }

    [Fact]
    public async Task A_refusal_reports_no_image_with_the_reason_and_empty_output()
    {
        var (code, stdout, stderr) = await RunAsync(
            "xclip", "-selection clipboard -t image/png -o", ClipboardImage.Refusal("pane p3 is not focused in any viewer"));

        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("not focused", stderr);
    }

    [Fact]
    public async Task Another_image_type_never_asks_the_viewer()
    {
        var clipboard = new CountingClipboard(new ClipboardImage(Png));

        await ClipboardShim.RunAsync(
            ClipboardShim.Parse("wl-paste", ["--type", "image/gif"]), clipboard, new MemoryStream(), new StringWriter());

        Assert.Equal(0, clipboard.Reads);
    }

    private static async Task<(int Code, byte[] Stdout, string Stderr)> RunAsync(string tool, string args, ClipboardImage image)
    {
        var stdout = new MemoryStream();
        var stderr = new StringWriter();
        var code = await ClipboardShim.RunAsync(ClipboardShim.Parse(tool, Split(args)), new CountingClipboard(image), stdout, stderr);
        return (code, stdout.ToArray(), stderr.ToString());
    }

    private static string[] Split(string args) => args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private sealed class CountingClipboard(ClipboardImage image) : IClipboardImageSource
    {
        public int Reads { get; private set; }

        public Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(image);
        }
    }
}
