using Fleet.Features.Todos.ShowBoard;
using Fleet.Features.Todos.ShowBoard.Models;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;
using Fleet.Tests.Ui.Harnesses;
using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Todos;

// Drives the real board view with keys and asserts what lands in the ticket store.
[Collection(ModalDepthCollection.Name)]
public sealed class BoardViewHarnessTests
{
    private const string Root = "/p";

    private static readonly DateTimeOffset Day = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private const string OtherRoot = "/q";

    private static readonly BoardProject Project = new("p", Root);

    private static readonly BoardProject Other = new("q", OtherRoot);

    private readonly StoreFake _store = new();

    private HeadlessRun<bool> Run(params Key[] keys)
    {
        var scopes = new BoardScopes(BoardScope.One("p", Root), BoardScope.All([Project, Other]));

        return HeadlessUi.Run(
            app =>
            {
                BoardView.Show(
                    app,
                    Keymap.Default,
                    scopes,
                    new BoardTickets(_store),
                    (_, _) => Task.FromResult(Result.Ok()),
                    (_, id) => Task.FromResult(Result<Ticket>.Fail($"cannot launch {id}")),
                    []);
                return true;
            },
            keys);
    }

    private static Ticket Open(string id, int day) =>
        new(id, $"title of {id}", null, TicketStatus.Open, TicketSource.Local, Day.AddDays(day), "body\n");

    [Fact]
    public void E_cycles_the_effort_of_the_selected_ticket_and_the_board_stays_open()
    {
        _store.Put(Open("0002-new", 2), Open("0001-old", 1));

        var run = Run(Key.E);

        Assert.True(run.LeftOpen);
        Assert.Equal(TicketEffort.XS, _store.Ticket("0002-new").Effort);
        Assert.Null(_store.Ticket("0001-old").Effort);
    }

    [Fact]
    public void X_closes_the_selected_ticket_and_the_next_x_closes_the_one_that_moved_up()
    {
        _store.Put(Open("0002-new", 2), Open("0001-old", 1));

        var run = Run(Key.X, Key.X);

        Assert.True(run.LeftOpen);
        Assert.Equal(TicketStatus.Closed, _store.Ticket("0002-new").Status);
        Assert.Equal(TicketStatus.Closed, _store.Ticket("0001-old").Status);
    }

    [Fact]
    public void Q_quits_the_board_without_touching_a_ticket()
    {
        _store.Put(Open("0001-only", 1));

        var run = Run(Key.Q);

        Assert.False(run.LeftOpen);
        Assert.False(run.WentBack);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void Backspace_goes_back_to_the_menu_it_came_from()
    {
        var run = Run(Key.Backspace);

        Assert.False(run.LeftOpen);
        Assert.True(run.WentBack);
    }

    [Fact]
    public void A_switches_to_all_projects_where_the_selection_can_move_to_another_projects_ticket()
    {
        _store.Put(OtherRoot, Open("0009-other", 9));
        _store.Put(Root, Open("0001-mine", 1));

        Run(Key.E);

        Assert.Equal(TicketEffort.XS, _store.Ticket(Root, "0001-mine").Effort);
        Assert.Null(_store.Ticket(OtherRoot, "0009-other").Effort);

        var all = Run(Key.A, Key.CursorUp, Key.E);

        Assert.True(all.LeftOpen);
        Assert.Equal(TicketEffort.XS, _store.Ticket(OtherRoot, "0009-other").Effort);
    }

    private sealed class StoreFake : ITodoStore
    {
        private readonly Dictionary<(string Root, string Id), Ticket> _tickets = [];

        public List<Ticket> Saved { get; } = [];

        public void Put(params Ticket[] tickets) => Put(Root, tickets);

        public void Put(string root, params Ticket[] tickets)
        {
            foreach (var ticket in tickets)
            {
                _tickets[(root, ticket.Id)] = ticket;
            }
        }

        public Ticket Ticket(string id) => Ticket(Root, id);

        public Ticket Ticket(string root, string id) => _tickets[(root, id)];

        public IReadOnlyList<Ticket> List(string projectRoot) =>
            [.. _tickets.Where(t => t.Key.Root == projectRoot).Select(t => t.Value)];

        public Ticket? Get(string projectRoot, string id) => _tickets.GetValueOrDefault((projectRoot, id));

        public Result<Ticket> Add(string projectRoot, Ticket draft) => Result<Ticket>.Fail("not used");

        public Result Save(string projectRoot, Ticket ticket)
        {
            Saved.Add(ticket);
            _tickets[(projectRoot, ticket.Id)] = ticket;
            return Result.Ok();
        }

        public Ticket? FindByOrchestration(string projectRoot, string orchestration) => null;
    }
}
