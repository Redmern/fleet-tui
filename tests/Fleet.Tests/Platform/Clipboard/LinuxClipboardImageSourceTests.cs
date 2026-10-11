using Fleet.Platform.Clipboard;

namespace Fleet.Tests.Platform.Clipboard;

public class LinuxClipboardImageSourceTests
{
    private const int Max = 64;

    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 5];

    [Fact]
    public async Task The_reader_stops_one_byte_past_the_limit_so_an_oversized_image_never_fills_memory()
    {
        var tools = new FakeTools { ["wl-paste"] = (0, new byte[Max + 1]) };

        var image = await new LinuxClipboardImageSource(Max, tools.RunAsync).ReadPngAsync();

        Assert.Equal([Max + 1], tools.Limits);
        Assert.Equal(Max + 1, image.Png!.Length);
    }

    [Theory]
    [InlineData("text/plain\nimage/png\n", true)]
    [InlineData("text/plain\nUTF8_STRING\n", false)]
    public async Task A_probe_lists_types_with_wl_paste_and_never_reads_the_image(string types, bool has)
    {
        var tools = new FakeTools { ["wl-paste"] = (0, System.Text.Encoding.UTF8.GetBytes(types)) };

        var probed = await new LinuxClipboardImageSource(Max, tools.RunAsync).ProbePngAsync();

        Assert.Equal(has, probed.HasPng);
        Assert.Null(probed.Png);
        Assert.Equal("wl-paste --list-types", tools.Ran[0]);
    }

    [Fact]
    public async Task A_probe_falls_back_to_xclip_targets()
    {
        var tools = new FakeTools { ["wl-paste"] = (1, []), ["xclip"] = (0, "TARGETS\nimage/png\n"u8.ToArray()) };

        var probed = await new LinuxClipboardImageSource(Max, tools.RunAsync).ProbePngAsync();

        Assert.True(probed.HasPng);
        Assert.Equal("xclip -selection clipboard -t TARGETS -o", tools.Ran[^1]);
    }

    [Fact]
    public async Task Wl_paste_is_asked_first_for_a_png()
    {
        var tools = new FakeTools { ["wl-paste"] = (0, Png), ["xclip"] = (0, [1, 2]) };

        var image = await new LinuxClipboardImageSource(Max, tools.RunAsync).ReadPngAsync();

        Assert.Equal(Png, image.Png);
        Assert.Equal(["wl-paste --no-newline --type image/png"], tools.Ran);
    }

    [Fact]
    public async Task Xclip_is_asked_when_wl_paste_has_no_image()
    {
        var tools = new FakeTools { ["wl-paste"] = (1, []), ["xclip"] = (0, Png) };

        var image = await new LinuxClipboardImageSource(Max, tools.RunAsync).ReadPngAsync();

        Assert.Equal(Png, image.Png);
        Assert.Equal("xclip -selection clipboard -t image/png -o", tools.Ran[^1]);
    }

    [Fact]
    public async Task Xclip_is_asked_when_wl_paste_is_not_installed()
    {
        var tools = new FakeTools { ["xclip"] = (0, Png) };

        Assert.Equal(Png, (await new LinuxClipboardImageSource(Max, tools.RunAsync).ReadPngAsync()).Png);
    }

    [Fact]
    public async Task Without_a_reader_or_an_image_there_is_no_image_and_no_refusal()
    {
        var none = await new LinuxClipboardImageSource(Max, new FakeTools().RunAsync).ReadPngAsync();
        var empty = await new LinuxClipboardImageSource(Max, new FakeTools { ["wl-paste"] = (1, []), ["xclip"] = (0, []) }.RunAsync).ReadPngAsync();

        Assert.Null(none.Png);
        Assert.Null(none.Refused);
        Assert.Null(empty.Png);
        Assert.Null(empty.Refused);
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

    [Fact]
    public async Task A_real_tool_run_returns_its_exit_code_and_raw_stdout_and_a_missing_tool_returns_nothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var ran = await ClipboardTools.RunAsync("sh", ["-c", "printf 'a\\001b'; exit 3"], 100, CancellationToken.None);
        var missing = await ClipboardTools.RunAsync($"no-such-tool-{Guid.NewGuid():N}", [], 100, CancellationToken.None);

        Assert.Equal(3, ran!.Code);
        Assert.Equal([(byte)'a', 1, (byte)'b'], ran.Stdout);
        Assert.False(ran.Truncated);
        Assert.Null(missing);
    }

    [Fact]
    public async Task A_real_tool_writing_more_than_the_limit_is_cut_off_at_the_limit()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var ran = await ClipboardTools.RunAsync("sh", ["-c", "head -c 5000000 /dev/zero"], 1001, CancellationToken.None);

        Assert.True(ran!.Truncated);
        Assert.Equal(1001, ran.Stdout.Length);
    }
}
