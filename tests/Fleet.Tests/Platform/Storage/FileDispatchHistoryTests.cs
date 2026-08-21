using Fleet.Platform.Storage;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class FileDispatchHistoryTests : ConfigHomeFixture
{
    private readonly FileDispatchHistory _history = new();

    [Fact]
    public void Remembers_prompts_most_recent_first()
    {
        _history.Add("techweb", "first task");
        _history.Add("techweb", "second task");

        Assert.Equal(["second task", "first task"], _history.List("techweb"));
    }

    [Fact]
    public void A_repeated_prompt_moves_to_the_front_instead_of_duplicating()
    {
        _history.Add("techweb", "first task");
        _history.Add("techweb", "second task");
        _history.Add("techweb", "first task");

        Assert.Equal(["first task", "second task"], _history.List("techweb"));
    }

    [Fact]
    public void Keeps_at_most_twenty_prompts()
    {
        for (var i = 0; i < 25; i++)
        {
            _history.Add("techweb", $"task {i}");
        }

        var listed = _history.List("techweb");

        Assert.Equal(FileDispatchHistory.Keep, listed.Count);
        Assert.Equal("task 24", listed[0]);
    }

    [Fact]
    public void Projects_do_not_share_history()
    {
        _history.Add("techweb", "task for techweb");

        Assert.Empty(_history.List("otherproject"));
    }

    [Fact]
    public void Blank_prompts_are_not_recorded()
    {
        _history.Add("techweb", "   ");

        Assert.Empty(_history.List("techweb"));
    }
}
