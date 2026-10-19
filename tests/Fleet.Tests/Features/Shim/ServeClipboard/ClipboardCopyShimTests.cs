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

    [Theory]
    [InlineData("wl-copy")]
    [InlineData("xclip")]
    public void Every_copy_tool_is_a_shimmed_tool(string tool) =>
        Assert.Contains(tool, ClipboardShim.Tools);

    [Theory]
    [InlineData("-i")]
    [InlineData("-in")]
    [InlineData("")]
    [InlineData("-selection clipboard")]
    [InlineData("-selection clipboard -i")]
    [InlineData("-sel clip -in")]
    [InlineData("-selection c -i")]
    [InlineData("-selection primary -i")]
    [InlineData("-selection p")]
    [InlineData("-selection secondary -i")]
    [InlineData("-sel s -i")]
    [InlineData("-quiet -i -selection clipboard")]
    [InlineData("-quiet -i -selection primary")]
    [InlineData("-silent -noutf8 -i")]
    [InlineData("-i -display :0 -selection clipboard")]
    [InlineData("-i -loops 1 -verbose")]
    [InlineData("-selection clipboard -t text/plain -i")]
    [InlineData("-selection clipboard -t text/plain;charset=utf-8 -i")]
    [InlineData("-selection clipboard -target UTF8_STRING -i")]
    [InlineData("-t STRING")]
    [InlineData("-t TEXT")]
    [InlineData("-f -selection clipboard")]
    [InlineData("-filter -i")]
    [InlineData("-rmlastnl -i")]
    public void Xclip_input_of_text_from_stdin_is_served(string args) =>
        Assert.Equal(ShimRead.Copy, ClipboardShim.Parse("xclip", Split(args)).Read);

    [Theory]
    [InlineData("-selection clipboard -t image/png -i")]
    [InlineData("-selection clipboard -t image/png -i shot.png")]
    [InlineData("-i notes.txt")]
    [InlineData("-selection clipboard -t text/html -i")]
    [InlineData("-selection bogus -i")]
    [InlineData("-selection")]
    [InlineData("-t")]
    [InlineData("-loops")]
    [InlineData("-version")]
    [InlineData("-help")]
    [InlineData("-selection clipboard -o")]
    [InlineData("-selection primary -o")]
    [InlineData("-selection clipboard -t UTF8_STRING -o")]
    public void Every_other_xclip_call_goes_to_the_real_tool(string args) =>
        Assert.Equal(ShimRead.RealTool, ClipboardShim.Parse("xclip", Split(args)).Read);

    [Theory]
    [InlineData("-selection clipboard -t image/png -o", ShimRead.Png)]
    [InlineData("-o -selection c -target image/png", ShimRead.Png)]
    [InlineData("-selection clipboard -t TARGETS -o", ShimRead.Types)]
    [InlineData("-selection clipboard -t image/jpeg -o", ShimRead.OtherImage)]
    public void Xclip_image_reads_are_unchanged(string args, ShimRead expected) =>
        Assert.Equal(expected, ClipboardShim.Parse("xclip", Split(args)).Read);

    [Theory]
    [InlineData("xclip", "-quiet -i -selection clipboard")]
    [InlineData("xclip", "-quiet -i -selection primary")]
    public async Task The_copies_nvim_makes_reach_the_viewer_without_the_real_tool(string tool, string args)
    {
        var (code, sink, stdout, stderr) = await ToolCopyAsync(tool, args, "yanked\n");

        Assert.Equal(0, code);
        Assert.Equal(["yanked\n"], sink.Copied);
        Assert.Empty(stdout);
        Assert.Empty(stderr);
    }

    [Fact]
    public async Task Xclip_rmlastnl_drops_one_trailing_newline()
    {
        var (code, sink, _, _) = await ToolCopyAsync("xclip", "-rmlastnl -i", "text\n\n");

        Assert.Equal(0, code);
        Assert.Equal(["text\n"], sink.Copied);
    }

    [Theory]
    [InlineData("-f")]
    [InlineData("-filter -selection clipboard")]
    public async Task Xclip_filter_copies_and_echoes_stdin(string args)
    {
        var (code, sink, stdout, _) = await ToolCopyAsync("xclip", args, "piped\n");

        Assert.Equal(0, code);
        Assert.Equal(["piped\n"], sink.Copied);
        Assert.Equal("piped\n", stdout);
    }

    [Fact]
    public async Task Xclip_filter_still_echoes_stdin_when_the_copy_is_refused()
    {
        var (code, _, stdout, stderr) = await ToolCopyAsync("xclip", "-f", "piped\n", refusal: "no viewer is attached to take the copy");

        Assert.Equal(1, code);
        Assert.Equal("piped\n", stdout);
        Assert.Contains("no viewer", stderr);
    }

    [Fact]
    public async Task Xclip_filter_echoes_all_of_stdin_over_the_cap()
    {
        var text = new string('a', ClipboardCopy.MaxBytes + 10);

        var (code, sink, stdout, stderr) = await ToolCopyAsync("xclip", "-f", text);

        Assert.NotEqual(0, code);
        Assert.Empty(sink.Copied);
        Assert.Equal(text.Length, stdout.Length);
        Assert.Contains("too large", stderr);
    }

    [Fact]
    public async Task Without_filter_nothing_is_echoed()
    {
        var (_, _, stdout, _) = await ToolCopyAsync("xclip", "-i", "quiet\n");

        Assert.Empty(stdout);
    }

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

    private static async Task<(int Code, RecordingSink Sink, string Stdout, string Stderr)> ToolCopyAsync(
        string tool, string args, string stdin, string? refusal = null)
    {
        var sink = new RecordingSink(refusal);
        var stdout = new MemoryStream();
        var stderr = new StringWriter();
        var code = await ClipboardShim.CopyAsync(
            ClipboardShim.Parse(tool, Split(args)), new MemoryStream(Encoding.UTF8.GetBytes(stdin)), sink, stderr, stdout);
        return (code, sink, Encoding.UTF8.GetString(stdout.ToArray()), stderr.ToString());
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
