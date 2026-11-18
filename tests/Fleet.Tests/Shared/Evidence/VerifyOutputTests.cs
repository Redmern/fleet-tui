using System.Text;
using Fleet.Shared.Evidence;

namespace Fleet.Tests.Shared.Evidence;

public sealed class VerifyOutputTests
{
    private static string Lines(int count) =>
        string.Join('\n', Enumerable.Range(1, count).Select(i => $"line {i}"));

    [Fact]
    public void Short_output_is_left_as_it_is()
    {
        Assert.Equal("Passed!  - Failed: 0\n", VerifyOutput.Tail("Passed!  - Failed: 0\n"));
        Assert.Equal(string.Empty, VerifyOutput.Tail(string.Empty));
    }

    [Fact]
    public void Exactly_forty_lines_are_kept_whole()
    {
        Assert.Equal(Lines(40), VerifyOutput.Tail(Lines(40)));
    }

    [Fact]
    public void More_than_forty_lines_keep_the_last_forty()
    {
        var kept = VerifyOutput.Tail(Lines(100));

        Assert.Equal(Lines(100).Split('\n')[60..], kept.Split('\n'));
        Assert.StartsWith("line 61", kept);
        Assert.EndsWith("line 100", kept);
    }

    [Fact]
    public void A_trailing_newline_does_not_count_as_a_line()
    {
        var kept = VerifyOutput.Tail(Lines(41) + "\n");

        Assert.StartsWith("line 2\n", kept);
        Assert.EndsWith("line 41", kept);
        Assert.Equal(40, kept.Split('\n').Length);
    }

    [Fact]
    public void Windows_line_endings_count_as_lines_too()
    {
        var kept = VerifyOutput.Tail(Lines(60).Replace("\n", "\r\n"));

        Assert.Equal(40, kept.Split('\n').Length);
        Assert.StartsWith("line 21", kept);
    }

    [Fact]
    public void Output_over_four_kilobytes_keeps_the_tail_within_the_limit()
    {
        var wide = string.Join('\n', Enumerable.Range(1, 30).Select(i => $"{i:00} " + new string('x', 497)));

        var kept = VerifyOutput.Tail(wide);

        Assert.True(Encoding.UTF8.GetByteCount(kept) <= VerifyOutput.MaxBytes);
        Assert.True(Encoding.UTF8.GetByteCount(kept) > VerifyOutput.MaxBytes - 4);
        Assert.EndsWith("30 " + new string('x', 497), kept);
        Assert.DoesNotContain("01 x", kept);
    }

    [Fact]
    public void One_giant_line_is_cut_from_the_front_not_the_end()
    {
        var kept = VerifyOutput.Tail(new string('a', 10_000) + "THE END");

        Assert.Equal(VerifyOutput.MaxBytes, Encoding.UTF8.GetByteCount(kept));
        Assert.EndsWith("THE END", kept);
    }

    [Fact]
    public void A_cut_never_splits_a_multibyte_character()
    {
        var kept = VerifyOutput.Tail(new string('é', 5000));

        Assert.True(Encoding.UTF8.GetByteCount(kept) <= VerifyOutput.MaxBytes);
        Assert.DoesNotContain('�', kept);
        Assert.All(kept, c => Assert.Equal('é', c));
    }
}
