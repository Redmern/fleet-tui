using Fleet.Features.Todos.ShowBoard;
using Fleet.Features.Todos.ShowBoard.Enums;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Todos;

public class BoardKeysTests
{
    private static readonly Keymap Keys = Keymap.Default;

    [Fact]
    public void E_cycles_effort_x_closes_r_refreshes_and_q_quits()
    {
        Assert.Equal(BoardCommand.Effort, BoardKeys.For(Key.E, Keys));
        Assert.Equal(BoardCommand.Close, BoardKeys.For(Key.X, Keys));
        Assert.Equal(BoardCommand.Refresh, BoardKeys.For(Key.R, Keys));
        Assert.Equal(BoardCommand.Quit, BoardKeys.For(Key.Q, Keys));
    }

    [Fact]
    public void Enter_is_left_to_the_list_so_a_ticket_opens_once()
    {
        Assert.Equal(BoardCommand.None, BoardKeys.For(Key.Enter, Keys));
    }

    [Fact]
    public void The_launch_key_l_stays_free()
    {
        Assert.Equal(BoardCommand.None, BoardKeys.For(Key.L, Keys));
    }

    [Fact]
    public void Board_keys_never_take_a_vim_motion()
    {
        FleetAction[] motions =
            [FleetAction.MoveDown, FleetAction.MoveUp, FleetAction.MoveFirst, FleetAction.MoveLast, FleetAction.PageDown, FleetAction.PageUp];

        Assert.All(motions, m => Assert.Equal(BoardCommand.None, BoardKeys.For(Keys.KeyFor(m), Keys)));
    }

    [Fact]
    public void Refresh_and_quit_follow_rebound_keys()
    {
        var rebound = new Keymap(KeymapConfig.Default.With(FleetAction.Refresh, "F5").With(FleetAction.Close, "Ctrl+Q"));

        Assert.Equal(BoardCommand.Refresh, BoardKeys.For(rebound.KeyFor(FleetAction.Refresh), rebound));
        Assert.Equal(BoardCommand.Quit, BoardKeys.For(rebound.KeyFor(FleetAction.Close), rebound));
        Assert.Equal(BoardCommand.None, BoardKeys.For(Key.R, rebound));
    }

    [Fact]
    public void The_action_bar_names_every_board_key()
    {
        var hints = BoardKeys.Hints(Keys).Select(h => h.Key).ToList();

        Assert.Equal(["enter", "e", "x", "r", "q"], hints);
    }
}
