using System.Text;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Input;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class SizeQueriesTests
{
    private static ReadOnlySpan<byte> Bytes(string text) => Encoding.ASCII.GetBytes(text);

    [Fact]
    public void Finds_the_text_area_size_query_among_other_output()
    {
        var queries = new SizeQueries();

        Assert.Equal(1, queries.Count(Bytes("\e[?2004h\e[18t\e[6n")));
        Assert.Equal(2, queries.Count(Bytes("\e[18t\e[18t")));
        Assert.Equal(0, queries.Count(Bytes("\e[14t\e[1;18H plain 18t")));
    }

    [Fact]
    public void Finds_a_query_split_across_reads()
    {
        var queries = new SizeQueries();

        Assert.Equal(0, queries.Count(Bytes("abc\e[1")));
        Assert.Equal(1, queries.Count(Bytes("8tdef")));
    }

    [Fact]
    public void An_escape_restarts_the_match()
    {
        Assert.Equal(1, new SizeQueries().Count(Bytes("\e[\e[18t")));
    }

    [Fact]
    public void The_reply_reports_rows_then_columns()
    {
        Assert.Equal("\e[8;39;120t", Encoding.ASCII.GetString(SizeQueries.Reply(120, 39)));
    }

    [Fact]
    public async Task A_pane_that_asks_for_its_size_is_told_its_current_size()
    {
        var panes = new FakePanes();
        using var runtime = new PaneRuntime("p1", panes.NewPty(), panes.NewTerminal, 80, 24);
        var pty = (FakePanes.FakePty)runtime.Pty;

        runtime.Feed(Encoding.ASCII.GetBytes("\e[18t"), 5);
        runtime.Resize(100, 30);
        runtime.Feed(Encoding.ASCII.GetBytes("\e[18t"), 5);

        for (var i = 0; i < 50 && !pty.Written.Contains("\e[8;30;100t", StringComparison.Ordinal); i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal("\e[8;24;80t\e[8;30;100t", pty.Written);
    }
}
