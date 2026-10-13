using System.Text;
using Fleet.Platform.Mux.Embedded.Client;
using Fleet.Platform.Mux.Embedded.Host;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class AttachClipboardTests
{
    private readonly List<string> _log = [];
    private readonly List<string> _notices = [];
    private readonly StringBuilder _written = new();

    [Fact]
    public void A_copy_the_windows_clipboard_takes_is_logged_with_no_notice_and_no_osc_52()
    {
        AttachClient.CopyOnWindows("yanked", _ => null, Write, _log.Add, _notices.Add);

        Assert.Contains(_log, l => l.Contains("clipboard: 6 chars set", StringComparison.Ordinal));
        Assert.Empty(_notices);
        Assert.Equal(string.Empty, _written.ToString());
    }

    [Fact]
    public void A_copy_the_windows_clipboard_refuses_falls_back_to_osc_52_and_shows_a_notice()
    {
        AttachClient.CopyOnWindows("yanked", _ => "SetClipboardData failed (error 1418)", Write, _log.Add, _notices.Add);

        Assert.Equal(HostEffectsOut.ClipboardSequence("yanked"), _written.ToString());
        Assert.Contains(_log, l => l.Contains("NOT set", StringComparison.Ordinal) && l.Contains("error 1418", StringComparison.Ordinal));
        var notice = Assert.Single(_notices);
        Assert.Contains("error 1418", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_copy_too_large_for_osc_52_still_shows_a_notice_when_windows_refuses_it()
    {
        var huge = new string('x', Fleet.Platform.Mux.Embedded.Native.GhosttyTerminal.MaxClipboard + 1);

        AttachClient.CopyOnWindows(huge, _ => "OpenClipboard failed (error 5)", Write, _log.Add, _notices.Add);

        Assert.Equal(string.Empty, _written.ToString());
        Assert.Single(_notices);
    }

    private void Write(byte[] bytes) => _written.Append(Encoding.UTF8.GetString(bytes));
}
