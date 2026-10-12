using Fleet.Features.Todos.ShowBoard;
using Fleet.Features.Todos.ShowBoard.Enums;
using Fleet.Features.Todos.ShowBoard.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Todos;

public class BoardKeysTests
{
    private static readonly Keymap Keys = Keymap.Default;

    private static readonly BoardScopes Scopes = new(BoardScope.One("alpha", "/a"), BoardScope.All([new BoardProject("alpha", "/a")]));

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
    public void L_launches_the_selected_ticket()
    {
        Assert.Equal(BoardCommand.Launch, BoardKeys.For(Key.L, Keys));
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
        var hints = BoardKeys.Hints(Keys, Scopes).Select(h => h.Key).ToList();

        Assert.Equal(["enter", "l", "e", "x", "a", "r", "q"], hints);
    }

    [Fact]
    public void A_switches_the_scope()
    {
        Assert.Equal(BoardCommand.Scope, BoardKeys.For(Key.A, Keys));
    }

    [Fact]
    public void The_scope_key_takes_no_default_board_or_list_key()
    {
        FleetAction[] taken =
        [
            FleetAction.MoveDown, FleetAction.MoveUp, FleetAction.MoveFirst, FleetAction.MoveLast,
            FleetAction.PageDown, FleetAction.PageUp, FleetAction.Refresh, FleetAction.Close,
        ];

        Assert.All(taken, a => Assert.NotEqual(BoardKeys.Scope, Keys.KeyFor(a)));
        Assert.DoesNotContain(BoardKeys.Scope, new[] { BoardKeys.Effort, BoardKeys.Close, BoardKeys.Launch });
    }

    [Fact]
    public void The_scope_hint_names_the_scope_the_key_switches_to()
    {
        string Label(BoardScopes scopes) => BoardKeys.Hints(Keys, scopes).Single(h => h.Command == BoardCommand.Scope).Label;

        Assert.Equal("all projects", Label(Scopes));
        Assert.Equal("this project", Label(Scopes.Switched()));
    }
}
