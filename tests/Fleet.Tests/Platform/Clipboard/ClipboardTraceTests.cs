using System.Text.Json;
using Fleet.Platform.Clipboard;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Native;

namespace Fleet.Tests.Platform.Clipboard;

public sealed class ClipboardTraceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fleet-clip-trace-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void A_trace_appends_one_json_line_per_hop_with_the_payload_sha256()
    {
        var path = Path.Combine(_dir, "trace.ndjson");
        var trace = new ClipboardTrace(path, new FixedTime(DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_123)));

        trace.Write("pane-osc52", "héllo", "p3");
        trace.Write("copy-target", "héllo", "p3", "c1");

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        var first = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal(1_700_000_000_123, first.GetProperty("ts").GetInt64());
        Assert.Equal("pane-osc52", first.GetProperty("hop").GetString());
        Assert.Equal(ClipboardTrace.Sha256("héllo"), first.GetProperty("sha256").GetString());
        Assert.Equal(6, first.GetProperty("bytes").GetInt32());
        Assert.Equal("c1", JsonDocument.Parse(lines[1]).RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public void A_trace_without_a_path_writes_nothing_and_never_throws()
    {
        ClipboardTrace.Off.Write("pane-osc52", "text");
        new ClipboardTrace(Path.Combine(_dir, "missing", "dir", "trace.ndjson")).Write("pane-osc52", "text");

        Assert.False(ClipboardTrace.Off.On);
        Assert.Empty(Directory.GetFiles(_dir, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void The_sha256_is_the_lower_hex_digest_of_the_utf8_text()
    {
        Assert.Equal("2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824", ClipboardTrace.Sha256("hello"));
    }

    [Fact]
    public void A_sink_appends_each_copy_as_a_json_line_instead_of_the_system_clipboard()
    {
        var path = Path.Combine(_dir, "sink.ndjson");
        var tracePath = Path.Combine(_dir, "trace.ndjson");
        var log = new List<string>();

        AttachClient.CopyToSink("first\nline", new ClipboardSink(path), log.Add, new ClipboardTrace(tracePath));
        AttachClient.CopyToSink("second", new ClipboardSink(path), log.Add, ClipboardTrace.Off);

        var lines = File.ReadAllLines(path).Select(l => JsonDocument.Parse(l).RootElement).ToList();
        Assert.Equal(["first\nline", "second"], lines.Select(l => l.GetProperty("text").GetString()));
        Assert.Equal(ClipboardTrace.Sha256("second"), lines[1].GetProperty("sha256").GetString());
        Assert.Equal(10, lines[0].GetProperty("chars").GetInt32());
        Assert.Contains(log, l => l == $"clipboard: 10 chars written to the sink {path}");
        Assert.Equal("viewer-sink", JsonDocument.Parse(Assert.Single(File.ReadAllLines(tracePath))).RootElement.GetProperty("hop").GetString());
    }

    [Fact]
    public void A_sink_that_cannot_be_written_logs_why()
    {
        var path = Path.Combine(_dir, "missing", "sink.ndjson");
        var log = new List<string>();

        AttachClient.CopyToSink("text", new ClipboardSink(path), log.Add, ClipboardTrace.Off);

        Assert.Contains(log, l => l.StartsWith($"clipboard: 4 chars NOT written to the sink {path}: ", StringComparison.Ordinal));
    }

    [Fact]
    public void The_terminal_takes_the_first_plain_text_content_of_an_osc_52_copy()
    {
        Assert.Equal((1, null), GhosttyTerminal.PickText([("image/png", 10UL), ("text/plain;charset=utf-8", 4UL)]));
        Assert.Equal((0, null), GhosttyTerminal.PickText([(string.Empty, 4UL)]));
    }

    [Fact]
    public void The_terminal_names_why_it_drops_an_osc_52_copy()
    {
        Assert.Equal("the copy carried no content", GhosttyTerminal.PickText([]).Dropped);
        Assert.Equal("no text/plain content, only image/png", GhosttyTerminal.PickText([("image/png", 10UL)]).Dropped);
        Assert.Equal(
            $"{GhosttyTerminal.MaxClipboard + 1} bytes of text is over the {GhosttyTerminal.MaxClipboard} byte limit",
            GhosttyTerminal.PickText([("text/plain", (ulong)GhosttyTerminal.MaxClipboard + 1)]).Dropped);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
