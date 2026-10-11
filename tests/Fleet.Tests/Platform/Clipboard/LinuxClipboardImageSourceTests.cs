using Fleet.Platform.Clipboard;

namespace Fleet.Tests.Platform.Clipboard;

public class LinuxClipboardImageSourceTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 5];

    [Fact]
    public async Task Wl_paste_is_asked_first_for_a_png()
    {
        var tools = new FakeTools { ["wl-paste"] = (0, Png), ["xclip"] = (0, [1, 2]) };

        var image = await new LinuxClipboardImageSource(tools.RunAsync).ReadPngAsync();

        Assert.Equal(Png, image.Png);
        Assert.Equal(["wl-paste --no-newline --type image/png"], tools.Ran);
    }

    [Fact]
    public async Task Xclip_is_asked_when_wl_paste_has_no_image()
    {
        var tools = new FakeTools { ["wl-paste"] = (1, []), ["xclip"] = (0, Png) };

        var image = await new LinuxClipboardImageSource(tools.RunAsync).ReadPngAsync();

        Assert.Equal(Png, image.Png);
        Assert.Equal("xclip -selection clipboard -t image/png -o", tools.Ran[^1]);
    }

    [Fact]
    public async Task Xclip_is_asked_when_wl_paste_is_not_installed()
    {
        var tools = new FakeTools { ["xclip"] = (0, Png) };

        Assert.Equal(Png, (await new LinuxClipboardImageSource(tools.RunAsync).ReadPngAsync()).Png);
    }

    [Fact]
    public async Task Without_a_reader_or_an_image_there_is_no_image_and_no_refusal()
    {
        var none = await new LinuxClipboardImageSource(new FakeTools().RunAsync).ReadPngAsync();
        var empty = await new LinuxClipboardImageSource(new FakeTools { ["wl-paste"] = (1, []), ["xclip"] = (0, []) }.RunAsync).ReadPngAsync();

        Assert.Null(none.Png);
        Assert.Null(none.Refused);
        Assert.Null(empty.Png);
        Assert.Null(empty.Refused);
    }

    private sealed class FakeTools : Dictionary<string, (int Code, byte[] Stdout)>
    {
        public List<string> Ran { get; } = [];

        public Task<(int Code, byte[] Stdout)?> RunAsync(string program, IReadOnlyList<string> args, CancellationToken ct)
        {
            Ran.Add(string.Join(' ', [program, .. args]));
            return Task.FromResult<(int Code, byte[] Stdout)?>(TryGetValue(program, out var result) ? result : null);
        }
    }

    [Fact]
    public async Task A_real_tool_run_returns_its_exit_code_and_raw_stdout_and_a_missing_tool_returns_nothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var ran = await ClipboardTools.RunAsync("sh", ["-c", "printf 'a\\001b'; exit 3"], CancellationToken.None);
        var missing = await ClipboardTools.RunAsync($"no-such-tool-{Guid.NewGuid():N}", [], CancellationToken.None);

        Assert.Equal(3, ran!.Value.Code);
        Assert.Equal([(byte)'a', 1, (byte)'b'], ran.Value.Stdout);
        Assert.Null(missing);
    }
}
