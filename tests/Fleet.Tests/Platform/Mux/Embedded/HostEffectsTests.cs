using System.Text;
using Fleet.Platform.Clipboard;
using Fleet.Platform.Mux.Embedded.Host;

namespace Fleet.Tests.Platform.Mux.Embedded;

public class HostEffectsTests
{
    [Fact]
    public void A_title_is_written_as_osc_0()
    {
        Assert.Equal("\e]0;nvim · techweb\a", HostEffectsOut.TitleSequence("nvim · techweb"));
    }

    [Fact]
    public void A_title_cannot_carry_escape_sequences_to_the_host()
    {
        var title = HostEffectsOut.Sanitize("evil\a\e]52;c;cm0gLXJm\a\r\n");

        Assert.DoesNotContain('\e', title);
        Assert.DoesNotContain('\a', title);
        Assert.Equal("evil]52;c;cm0gLXJm", title);
    }

    [Fact]
    public void A_title_is_capped_in_length()
    {
        Assert.Equal(HostEffectsOut.MaxTitle, HostEffectsOut.Sanitize(new string('x', 1000)).Length);
    }

    [Fact]
    public void The_clipboard_goes_to_the_host_as_osc_52_base64_utf8()
    {
        var sequence = HostEffectsOut.ClipboardSequence("héllo\n");

        Assert.Equal($"\e]52;c;{Convert.ToBase64String(Encoding.UTF8.GetBytes("héllo\n"))}\a", sequence);
    }

    [Fact]
    public async Task A_copy_writes_osc_52_and_also_hands_the_text_to_a_native_tool()
    {
        var written = new List<byte[]>();
        var fed = new List<string>();
        var native = ClipboardText.Linux(64, (program, _, input, _) =>
        {
            fed.Add($"{program}:{Encoding.UTF8.GetString(input)}");
            return Task.FromResult<int?>(program == "xclip" ? 0 : null);
        });

        var copied = await HostEffectsOut.CopyTextAsync("yank", written.Add, native);

        Assert.Equal(HostEffectsOut.ClipboardSequence("yank"), Encoding.UTF8.GetString(written.Single()));
        Assert.Equal(["wl-copy:yank", "xclip:yank"], fed);
        Assert.True(copied.Done);
        Assert.Equal("xclip", copied.By);
    }

    [Fact]
    public async Task A_copy_fails_only_when_no_native_tool_takes_it()
    {
        var written = new List<byte[]>();
        var native = ClipboardText.Linux(64, (_, _, _, _) => Task.FromResult<int?>(null));

        var copied = await HostEffectsOut.CopyTextAsync("yank", written.Add, native);

        Assert.Single(written);
        Assert.False(copied.Done);
        Assert.Null(copied.By);
    }

    [Fact]
    public async Task A_copy_without_native_tools_still_writes_osc_52_but_cannot_confirm_it()
    {
        var written = new List<byte[]>();

        var copied = await HostEffectsOut.CopyTextAsync("yank", written.Add, null);

        Assert.Single(written);
        Assert.False(copied.Done);
    }

    [Fact]
    public async Task The_paste_chord_reads_text_through_the_native_tools_off_windows()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var native = ClipboardText.Linux(64, run: (program, _, limit, _) =>
            Task.FromResult<ClipboardRun?>(program == "wl-paste" ? new ClipboardRun(0, "pasted"u8.ToArray(), false) : null));

        Assert.Equal("pasted", await HostEffectsOut.ReadTextAsync(native));
        Assert.Null(await HostEffectsOut.ReadTextAsync(null));
    }
}
