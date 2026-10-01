using System.Text;
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
}
