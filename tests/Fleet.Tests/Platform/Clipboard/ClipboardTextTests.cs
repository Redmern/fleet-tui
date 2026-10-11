using System.Text;
using Fleet.Platform.Clipboard;

namespace Fleet.Tests.Platform.Clipboard;

public class ClipboardTextTests
{
    private const int Max = 64;

    [Fact]
    public async Task A_copy_goes_to_wl_copy_first_as_utf8_on_stdin()
    {
        var tools = new FakeTools { Exits = { ["wl-copy"] = 0 } };

        var by = await ClipboardText.Linux(Max, tools.FeedAsync, tools.RunAsync).CopyAsync("héllo");

        Assert.Equal("wl-copy", by);
        Assert.Equal(["wl-copy"], tools.Ran);
        Assert.Equal(Encoding.UTF8.GetBytes("héllo"), tools.Fed[0]);
    }

    [Fact]
    public async Task A_copy_falls_back_to_xclip_when_wl_copy_is_missing_or_fails()
    {
        var tools = new FakeTools { Exits = { ["wl-copy"] = 1, ["xclip"] = 0 } };

        var by = await ClipboardText.Linux(Max, tools.FeedAsync, tools.RunAsync).CopyAsync("x");

        Assert.Equal("xclip", by);
        Assert.Equal(["wl-copy", "xclip -selection clipboard -i"], tools.Ran);
    }

    [Fact]
    public async Task A_copy_reports_failure_only_when_every_tool_fails()
    {
        var tools = new FakeTools();

        var by = await ClipboardText.Linux(Max, tools.FeedAsync, tools.RunAsync).CopyAsync("x");

        Assert.Null(by);
        Assert.Equal(2, tools.Ran.Count);
    }

    [Fact]
    public async Task A_mac_copy_goes_to_pbcopy()
    {
        var tools = new FakeTools { Exits = { ["pbcopy"] = 0 } };

        var by = await ClipboardText.Mac(Max, tools.FeedAsync, tools.RunAsync).CopyAsync("x");

        Assert.Equal("pbcopy", by);
    }

    [Fact]
    public async Task A_read_asks_wl_paste_without_a_trailing_newline()
    {
        var tools = new FakeTools { Outputs = { ["wl-paste"] = (0, "héllo"u8.ToArray()) } };

        var text = await ClipboardText.Linux(Max, tools.FeedAsync, tools.RunAsync).ReadAsync();

        Assert.Equal("héllo", text);
        Assert.Equal(["wl-paste --no-newline"], tools.Ran);
    }

    [Fact]
    public async Task A_read_falls_back_to_xclip()
    {
        var tools = new FakeTools { Outputs = { ["wl-paste"] = (1, []), ["xclip"] = (0, "x"u8.ToArray()) } };

        var text = await ClipboardText.Linux(Max, tools.FeedAsync, tools.RunAsync).ReadAsync();

        Assert.Equal("x", text);
        Assert.Equal("xclip -selection clipboard -o", tools.Ran[^1]);
    }

    [Fact]
    public async Task A_mac_read_goes_to_pbpaste()
    {
        var tools = new FakeTools { Outputs = { ["pbpaste"] = (0, "x"u8.ToArray()) } };

        Assert.Equal("x", await ClipboardText.Mac(Max, tools.FeedAsync, tools.RunAsync).ReadAsync());
    }

    [Fact]
    public async Task A_read_over_the_size_cap_pastes_nothing_rather_than_a_cut_off_text()
    {
        var tools = new FakeTools { Outputs = { ["wl-paste"] = (0, new byte[Max + 1]), ["xclip"] = (0, "x"u8.ToArray()) } };

        var text = await ClipboardText.Linux(Max, tools.FeedAsync, tools.RunAsync).ReadAsync();

        Assert.Null(text);
        Assert.Equal([Max + 1], tools.Limits);
    }

    [Fact]
    public async Task A_read_with_no_tool_or_an_empty_clipboard_returns_nothing()
    {
        var tools = new FakeTools { Outputs = { ["wl-paste"] = (0, []) } };

        Assert.Null(await ClipboardText.Linux(Max, tools.FeedAsync, tools.RunAsync).ReadAsync());
    }

    [Fact]
    public void Windows_has_no_native_text_tools_since_it_uses_the_win32_clipboard()
    {
        Assert.Equal(OperatingSystem.IsWindows(), ClipboardText.ForThisMachine(Max) is null);
    }

    [Fact]
    public async Task Feeding_a_real_tool_delivers_the_text_on_stdin()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var file = Path.Combine(Path.GetTempPath(), $"fleet-feed-{Guid.NewGuid():N}");
        try
        {
            var code = await ClipboardTextFeed.FeedAsync("sh", ["-c", $"cat > '{file}'"], "héllo"u8.ToArray(), TimeSpan.FromSeconds(10), default);

            Assert.Equal(0, code);
            Assert.Equal("héllo", await File.ReadAllTextAsync(file));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Feeding_a_tool_that_hangs_gives_up_within_the_timeout()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var started = System.Diagnostics.Stopwatch.StartNew();

        var code = await ClipboardTextFeed.FeedAsync("sh", ["-c", "sleep 30"], [], TimeSpan.FromMilliseconds(200), default);

        Assert.Null(code);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Feeding_a_missing_tool_returns_nothing()
    {
        var code = await ClipboardTextFeed.FeedAsync($"fleet-no-such-tool-{Guid.NewGuid():N}", [], [], TimeSpan.FromSeconds(5), default);

        Assert.Null(code);
    }

    private sealed class FakeTools
    {
        public Dictionary<string, int> Exits { get; } = [];

        public Dictionary<string, (int Code, byte[] Stdout)> Outputs { get; } = [];

        public List<string> Ran { get; } = [];

        public List<byte[]> Fed { get; } = [];

        public List<int> Limits { get; } = [];

        public Task<int?> FeedAsync(string program, IReadOnlyList<string> args, byte[] input, CancellationToken ct)
        {
            Ran.Add(string.Join(' ', [program, .. args]));
            Fed.Add(input);
            return Task.FromResult(Exits.TryGetValue(program, out var code) ? code : (int?)null);
        }

        public Task<ClipboardRun?> RunAsync(string program, IReadOnlyList<string> args, int limit, CancellationToken ct)
        {
            Ran.Add(string.Join(' ', [program, .. args]));
            Limits.Add(limit);
            if (!Outputs.TryGetValue(program, out var output))
            {
                return Task.FromResult<ClipboardRun?>(null);
            }

            var kept = output.Stdout.Take(limit).ToArray();
            return Task.FromResult<ClipboardRun?>(new ClipboardRun(output.Code, kept, kept.Length >= limit));
        }
    }
}
