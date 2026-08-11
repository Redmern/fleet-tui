using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Tests.Ui;

public class FleetRowSourceTests
{
    private static FleetRow Row(string text) =>
        new([FleetSpan.Plain(text), new FleetSpan("!", FleetTones.Ahead)]);

    [Fact]
    public void The_widest_row_sets_the_item_length()
    {
        var source = new FleetRowSource([Row("short"), Row("a-much-longer-row")]);

        Assert.Equal(2, source.Items);
        Assert.Equal("a-much-longer-row!".Length, source.MaxItemLength);
    }

    [Fact]
    public void Rows_are_spaced_by_a_blank_line_between_them_but_not_after_the_last()
    {
        var source = new FleetRowSource([Row("backend"), Row("frontend")], spaced: true);

        Assert.Equal(3, source.Count);
        Assert.True(source.Holds(0));
        Assert.False(source.Holds(1));
        Assert.True(source.Holds(2));
    }

    [Fact]
    public void An_item_maps_to_its_row_and_back()
    {
        var source = new FleetRowSource([Row("a"), Row("b"), Row("c")], spaced: true);

        Assert.Equal(4, source.IndexOf(2));
        Assert.Equal(2, source.ItemAt(4));
        Assert.Equal(1, source.ItemAt(2));
    }

    [Fact]
    public void Rows_map_one_to_one_unless_spacing_is_asked_for()
    {
        var source = new FleetRowSource([Row("a"), Row("b")]);

        Assert.Equal(2, source.Count);
        Assert.Equal(1, source.IndexOf(1));
        Assert.True(source.Holds(1));
    }

    [Fact]
    public void Spans_flatten_into_the_text_the_list_searches()
    {
        var source = new FleetRowSource([Row("backend")]);

        Assert.Equal(["backend!"], source.ToList().Cast<string>());
    }

    [Fact]
    public void A_row_can_be_swapped_in_place_for_the_pull_spinner()
    {
        var source = new FleetRowSource([Row("backend"), Row("frontend")]);

        source.Replace(1, FleetRow.Plain("pulling..."));
        source.Replace(9, FleetRow.Plain("ignored"));
        source.Replace(-1, FleetRow.Plain("ignored"));

        Assert.Equal(["backend!", "pulling..."], source.ToList().Cast<string>());
    }
}
