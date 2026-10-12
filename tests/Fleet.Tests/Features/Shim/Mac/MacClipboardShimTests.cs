using System.Text;
using Fleet.Features.Shim.Mac;
using Fleet.Features.Shim.Mac.Enums;
using Fleet.Ports;

namespace Fleet.Tests.Features.Shim.Mac;

public sealed class MacClipboardShimTests : IDisposable
{
    private const string Probe = "the clipboard as «class PNGf»";

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 7];

    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"fleet-mac-shim-{Guid.NewGuid():N}");

    public MacClipboardShimTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static string[] Save(string file) =>
    [
        "-e", "set png_data to (the clipboard as «class PNGf»)",
        "-e", $"set fp to open for access POSIX file \"{file}\" with write permission",
        "-e", "write png_data to fp",
        "-e", "close access fp",
    ];

    [Fact]
    public void Osascript_probing_for_a_png_is_served()
    {
        var call = MacClipboardShim.Parse("osascript", ["-e", Probe]);

        Assert.Equal(MacRead.Probe, call.Read);
    }

    [Theory]
    [InlineData("the clipboard as <<class PNGf>>")]
    [InlineData("(the clipboard as «class PNGf»)")]
    [InlineData("get the clipboard as «class PNGf»")]
    [InlineData("  the  clipboard as «class PNGf» ")]
    public void Osascript_probe_spellings_are_served(string script) =>
        Assert.Equal(MacRead.Probe, MacClipboardShim.Parse("osascript", ["-e", script]).Read);

    [Fact]
    public void Osascript_saving_the_png_to_a_file_is_served_with_its_path()
    {
        var call = MacClipboardShim.Parse("osascript", Save("/tmp/claude_cli_latest_screenshot.png"));

        Assert.Equal(MacRead.File, call.Read);
        Assert.Equal("/tmp/claude_cli_latest_screenshot.png", call.File);
    }

    [Fact]
    public void Osascript_save_script_in_one_argument_is_served()
    {
        var call = MacClipboardShim.Parse("osascript", ["-e", string.Join('\n', Save("/tmp/a b.png").Where((_, i) => i % 2 == 1))]);

        Assert.Equal(MacRead.File, call.Read);
        Assert.Equal("/tmp/a b.png", call.File);
    }

    [Fact]
    public void Osascript_save_path_with_escaped_quotes_is_unescaped()
    {
        var call = MacClipboardShim.Parse("osascript", Save("/tmp/say \\\"hi\\\".png"));

        Assert.Equal("/tmp/say \"hi\".png", call.File);
    }

    [Theory]
    [InlineData("-e|get POSIX path of (the clipboard as «class furl»)")]
    [InlineData("-e|the clipboard as «class TIFF»")]
    [InlineData("-e|the clipboard")]
    [InlineData("-e|display notification \"done\"")]
    [InlineData("script.scpt")]
    [InlineData("-l|JavaScript|-e|1")]
    [InlineData("")]
    public void Every_other_osascript_call_goes_to_the_real_tool(string args) =>
        Assert.Equal(MacRead.RealTool, MacClipboardShim.Parse("osascript", Split(args)).Read);

    [Fact]
    public void A_save_writing_another_variable_goes_to_the_real_tool()
    {
        string[] args =
        [
            "-e", "set png_data to (the clipboard as «class PNGf»)",
            "-e", "set fp to open for access POSIX file \"/tmp/x.png\" with write permission",
            "-e", "write other_data to fp",
            "-e", "close access fp",
        ];

        Assert.Equal(MacRead.RealTool, MacClipboardShim.Parse("osascript", args).Read);
    }

    [Theory]
    [InlineData("-", MacRead.Stdout)]
    [InlineData("shot.png", MacRead.File)]
    [InlineData("/tmp/shot", MacRead.File)]
    [InlineData("-b|-", MacRead.Base64)]
    public void Pngpaste_image_reads_are_served(string args, MacRead expected) =>
        Assert.Equal(expected, MacClipboardShim.Parse("pngpaste", Split(args)).Read);

    [Theory]
    [InlineData("")]
    [InlineData("-v")]
    [InlineData("shot.jpg")]
    [InlineData("a.png|b.png")]
    [InlineData("-x|a.png")]
    public void Every_other_pngpaste_call_goes_to_the_real_tool(string args) =>
        Assert.Equal(MacRead.RealTool, MacClipboardShim.Parse("pngpaste", Split(args)).Read);

    [Fact]
    public void Another_tool_goes_to_the_real_tool() =>
        Assert.Equal(MacRead.RealTool, MacClipboardShim.Parse("pbpaste", []).Read);

    [Fact]
    public async Task Osascript_probe_succeeds_without_reading_the_png_when_the_viewer_has_one()
    {
        var clipboard = new CountingClipboard(new ClipboardImage(Png));

        var (code, _, stderr) = await RunAsync(MacClipboardShim.Parse("osascript", ["-e", Probe]), clipboard);

        Assert.Equal(0, code);
        Assert.Empty(stderr);
        Assert.Equal(1, clipboard.Probes);
        Assert.Equal(0, clipboard.Reads);
    }

    [UnixFact]
    public async Task Osascript_save_writes_the_viewer_png_to_the_file()
    {
        var file = Path.Combine(_folder, "claude_cli_latest_screenshot.png");

        var (code, stdout, _) = await RunAsync(MacClipboardShim.Parse("osascript", Save(file)), new CountingClipboard(new ClipboardImage(Png)));

        Assert.Equal(0, code);
        Assert.Empty(stdout);
        Assert.Equal(Png, await File.ReadAllBytesAsync(file));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Osascript_without_an_image_fails_like_the_native_tool_and_writes_no_file(bool probe)
    {
        var file = Path.Combine(_folder, "shot.png");
        var call = MacClipboardShim.Parse("osascript", probe ? ["-e", Probe] : Save(file));

        var (code, stdout, stderr) = await RunAsync(call, new CountingClipboard(ClipboardImage.None));

        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("Can’t make the clipboard into type «class PNGf». (-1700)", stderr);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Pngpaste_to_stdout_writes_the_png_as_is()
    {
        var (code, stdout, _) = await RunAsync(MacClipboardShim.Parse("pngpaste", ["-"]), new CountingClipboard(new ClipboardImage(Png)));

        Assert.Equal(0, code);
        Assert.Equal(Png, stdout);
    }

    [Fact]
    public async Task Pngpaste_base64_writes_the_png_in_base64()
    {
        var (code, stdout, _) = await RunAsync(MacClipboardShim.Parse("pngpaste", ["-b", "-"]), new CountingClipboard(new ClipboardImage(Png)));

        Assert.Equal(0, code);
        Assert.Equal(Convert.ToBase64String(Png), Encoding.ASCII.GetString(stdout));
    }

    [Fact]
    public async Task Pngpaste_to_a_file_writes_the_png_there()
    {
        var file = Path.Combine(_folder, "shot.png");

        var (code, _, _) = await RunAsync(MacClipboardShim.Parse("pngpaste", [file]), new CountingClipboard(new ClipboardImage(Png)));

        Assert.Equal(0, code);
        Assert.Equal(Png, await File.ReadAllBytesAsync(file));
    }

    [Fact]
    public async Task Pngpaste_without_an_image_fails_like_the_native_tool()
    {
        var (code, stdout, stderr) = await RunAsync(MacClipboardShim.Parse("pngpaste", ["-"]), new CountingClipboard(ClipboardImage.None));

        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("No PNG data found on the clipboard!", stderr);
    }

    [Fact]
    public async Task A_refusal_reports_no_image_with_the_reason()
    {
        var (code, stdout, stderr) = await RunAsync(
            MacClipboardShim.Parse("pngpaste", ["-"]), new CountingClipboard(ClipboardImage.Refusal("pane p3 is not focused in any viewer")));

        Assert.Equal(1, code);
        Assert.Empty(stdout);
        Assert.Contains("not focused", stderr);
    }

    [Fact]
    public async Task A_file_that_cannot_be_written_fails_with_the_reason()
    {
        var file = Path.Combine(_folder, "missing", "shot.png");

        var (code, _, stderr) = await RunAsync(MacClipboardShim.Parse("pngpaste", [file]), new CountingClipboard(new ClipboardImage(Png)));

        Assert.Equal(1, code);
        Assert.Contains(file, stderr);
    }

    private static async Task<(int Code, byte[] Stdout, string Stderr)> RunAsync(Fleet.Features.Shim.Mac.Models.MacShimCall call, IClipboardImageSource clipboard)
    {
        var stdout = new MemoryStream();
        var stderr = new StringWriter();
        var code = await MacClipboardShim.RunAsync(call, clipboard, stdout, stderr);
        return (code, stdout.ToArray(), stderr.ToString());
    }

    private static string[] Split(string args) => args.Split('|', StringSplitOptions.RemoveEmptyEntries);

    private sealed class CountingClipboard(ClipboardImage image) : IClipboardImageSource
    {
        public int Reads { get; private set; }

        public int Probes { get; private set; }

        public Task<ClipboardImage> ProbePngAsync(CancellationToken ct = default)
        {
            Probes++;
            return Task.FromResult(image.HasPng ? ClipboardImage.Available : image with { Png = null });
        }

        public Task<ClipboardImage> ReadPngAsync(CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(image);
        }
    }

    private sealed class UnixFactAttribute : FactAttribute
    {
        public UnixFactAttribute()
        {
            if (OperatingSystem.IsWindows())
            {
                Skip = "osascript takes a POSIX file path, where a Windows path's backslashes are escapes; the shim only runs on a mac pane";
            }
        }
    }
}
