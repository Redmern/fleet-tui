using Fleet.Ui;
using Fleet.Ui.Models;

namespace Fleet.Tests.Ui;

public class EmptyRowsTests
{
    [Fact]
    public void Filling_a_list_with_no_rows_leaves_nothing_selected()
    {
        var list = new FleetList();

        FleetRows.Fill(list, [], 3);

        Assert.Null(list.SelectedItem);
    }

    [Fact]
    public void Emptying_a_filled_list_clears_the_selection()
    {
        var list = new FleetList();
        FleetRows.Fill(list, [FleetRow.Plain("a"), FleetRow.Plain("b")], 1);

        FleetRows.Fill(list, []);

        Assert.Null(list.SelectedItem);
    }

    [Fact]
    public void Rows_that_arrive_after_an_empty_fill_can_be_selected()
    {
        var list = new FleetList();
        FleetRows.Fill(list, []);

        FleetRows.Fill(list, [FleetRow.Plain("a"), FleetRow.Plain("b")], 1);

        Assert.Equal(1, FleetRows.Selected(list));
    }

    [Fact]
    public void An_empty_list_reports_the_first_item_as_selected()
    {
        var list = new FleetList();

        FleetRows.Fill(list, []);

        Assert.Equal(0, FleetRows.Selected(list));
    }
}
