using Fleet.Cli.Composition;

namespace Fleet.Tests.Cli;

public class TallyTests
{
    [Fact]
    public void Two_counts_are_read_left_then_right()
    {
        Assert.Equal((4, 1), Tally.Parse("4\t1"));
    }

    [Fact]
    public void Spaces_work_as_well_as_tabs()
    {
        Assert.Equal((0, 3), Tally.Parse("0 3"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("4")]
    [InlineData("not counts")]
    public void Anything_unparseable_counts_as_level(string output)
    {
        Assert.Equal((0, 0), Tally.Parse(output));
    }
}
