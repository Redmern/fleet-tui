using System.Diagnostics;
using Fleet.Platform.Forwards;

namespace Fleet.Tests.Platform.Forwards;

public sealed class SystemBrowserTests
{
    private static Func<ProcessStartInfo, Process?> Running(string script) =>
        _ => Process.Start(OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd") { ArgumentList = { "/c", script }, UseShellExecute = false, CreateNoWindow = true }
            : new ProcessStartInfo("sh") { ArgumentList = { "-c", script }, UseShellExecute = false });

    [Fact]
    public void An_opener_that_exits_cleanly_opened_the_url()
    {
        var browser = new SystemBrowser(Running("exit 0"), TimeSpan.FromSeconds(5));

        Assert.Null(browser.Open("http://localhost:5173"));
    }

    [Fact]
    public void An_opener_that_exits_with_an_error_reports_that_it_could_not_open_the_url()
    {
        var browser = new SystemBrowser(Running("exit 3"), TimeSpan.FromSeconds(5));

        var failed = browser.Open("http://localhost:5173");

        Assert.NotNull(failed);
        Assert.Contains("exit code 3", failed, StringComparison.Ordinal);
    }

    [Fact]
    public void An_opener_still_running_after_the_wait_counts_as_opened()
    {
        var browser = new SystemBrowser(
            Running(OperatingSystem.IsWindows() ? "ping -n 6 127.0.0.1 >NUL" : "sleep 5"), TimeSpan.FromMilliseconds(200));
        var clock = Stopwatch.StartNew();

        Assert.Null(browser.Open("http://localhost:5173"));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4));
    }

    [Fact]
    public void A_url_that_is_not_http_is_refused_without_starting_anything()
    {
        var started = false;
        var browser = new SystemBrowser(_ =>
        {
            started = true;
            return null;
        }, TimeSpan.FromSeconds(1));

        Assert.NotNull(browser.Open("file:///etc/passwd"));
        Assert.False(started);
    }
}
