using System.Text;
using Fleet.Features.Shim.ServeClipboard;
using Fleet.Features.Shim.ServeClipboard.Enums;
using Fleet.Platform.Mux.Embedded.Native;
using Fleet.Ports;

namespace Fleet.Tests.Features.Shim.ServeClipboard;

public class ClipboardCopyShimTests
{
    [Theory]
    [InlineData("")]
    [InlineData("-n")]
    [InlineData("--trim-newline")]
    [InlineData("-f")]
    [InlineData("--foreground")]
    [InlineData("-t text/plain")]
    [InlineData("--type text/plain;charset=utf-8")]
    [InlineData("--type=text/plain")]
    [InlineData("-n -f -t text/plain")]
    public void Wl_copy_of_text_from_stdin_is_served(string args) =>
        Assert.Equal(ShimRead.Copy, ClipboardShim.Parse("wl-copy", Split(args)).Read);

    [Theory]
    [InlineData("-p")]
    [InlineData("--primary")]
    [InlineData("-o")]
    [InlineData("--paste-once")]
    [InlineData("-c")]
    [InlineData("--clear")]
    [InlineData("-t image/png")]
    [InlineData("--type text/html")]
    [InlineData("--type")]
    [InlineData("--seat seat0")]
    [InlineData("hello")]
    [InlineData("-n hello")]
    [InlineData("--")]
    public void Every_other_wl_copy_call_goes_to_the_real_tool(string args) =>
        Assert.Equal(ShimRead.RealTool, ClipboardShim.Parse("wl-copy", Split(args)).Read);

    [Fact]
    public void Wl_copy_is_a_shimmed_tool() =>
        Assert.Contains("wl-copy", ClipboardShim.Tools);

    [Fact]
    public void The_copy_cap_is_the_osc_52_cap() =>
        Assert.Equal(GhosttyTerminal.MaxClipboard, ClipboardCopy.MaxBytes);

    [Fact]
    public async Task Stdin_is_copied_to_the_viewer_as_is()
    {
        var (code, sink, stderr) = await CopyAsync("", "line one\nline two\n");

        Assert.Equal(0, code);
        Assert.Equal(["line one\nline two\n"], sink.Copied);
        Assert.Empty(stderr);
    }

    [Theory]
    [InlineData("-n", "text\n", "text")]
    [InlineData("--trim-newline", "text\n\n", "text\n")]
    [InlineData("-n", "text", "text")]
    public async Task Trim_newline_drops_one_trailing_newline(string args, string stdin, string copied)
    {
        var (code, sink, _) = await CopyAsync(args, stdin);

        Assert.Equal(0, code);
        Assert.Equal([copied], sink.Copied);
    }

    [Fact]
    public async Task Stdin_at_the_cap_is_copied()
    {
        var (code, sink, _) = await CopyAsync("", new string('a', ClipboardCopy.MaxBytes));

        Assert.Equal(0, code);
        Assert.Equal(ClipboardCopy.MaxBytes, Assert.Single(sink.Copied).Length);
    }

    [Fact]
    public async Task Stdin_over_the_cap_fails_without_asking_fleetd()
    {
        var (code, sink, stderr) = await CopyAsync("", new string('a', ClipboardCopy.MaxBytes + 1));

        Assert.NotEqual(0, code);
        Assert.Empty(sink.Copied);
        Assert.Contains("too large", stderr);
    }

    [Fact]
    public async Task Stdin_that_is_not_utf8_text_fails_without_asking_fleetd()
    {
        var sink = new RecordingSink(null);
        var stderr = new StringWriter();

        var code = await ClipboardShim.CopyAsync(
            ClipboardShim.Parse("wl-copy", []), new MemoryStream([0xff, 0xfe, 0x00, 0x89]), sink, stderr);

        Assert.NotEqual(0, code);
        Assert.Empty(sink.Copied);
        Assert.Contains("text", stderr.ToString());
    }

    [Fact]
    public async Task A_refusal_fails_with_the_reason()
    {
        var (code, _, stderr) = await CopyAsync("", "text", refusal: "no viewer is attached to take the copy");

        Assert.Equal(1, code);
        Assert.Contains("no viewer", stderr);
    }

    private static async Task<(int Code, RecordingSink Sink, string Stderr)> CopyAsync(string args, string stdin, string? refusal = null)
    {
        var sink = new RecordingSink(refusal);
        var stderr = new StringWriter();
        var code = await ClipboardShim.CopyAsync(
            ClipboardShim.Parse("wl-copy", Split(args)), new MemoryStream(Encoding.UTF8.GetBytes(stdin)), sink, stderr);
        return (code, sink, stderr.ToString());
    }

    private static string[] Split(string args) => args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    private sealed class RecordingSink(string? refusal) : IClipboardTextSink
    {
        public List<string> Copied { get; } = [];

        public Task<ClipboardCopy> CopyAsync(string text, CancellationToken ct = default)
        {
            Copied.Add(text);
            return Task.FromResult(refusal is null ? ClipboardCopy.Done : ClipboardCopy.Refusal(refusal));
        }
    }
}
