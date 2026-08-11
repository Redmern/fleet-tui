using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Ui.Models;

namespace Fleet.Tests.Features.Dashboard;

public class AgentBoardTests
{
    private static readonly AgentBoard Board = new(
        [FleetRow.Plain("a"), FleetRow.Plain("b")], 2, [false, true]);

    [Fact]
    public void The_board_knows_which_rows_are_hidden_so_the_bar_can_say_show()
    {
        Assert.False(Board.IsHidden(0));
        Assert.True(Board.IsHidden(1));
    }

    [Fact]
    public void An_index_off_the_end_is_not_hidden_rather_than_a_crash()
    {
        Assert.False(Board.IsHidden(-1));
        Assert.False(Board.IsHidden(9));
        Assert.False(new AgentBoard([], 0, []).IsHidden(0));
    }
}
