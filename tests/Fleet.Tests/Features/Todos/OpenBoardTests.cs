using Fleet.Features.Todos.ShowBoard;
using Fleet.Features.Todos.ShowBoard.Models;
using Fleet.Platform.Storage;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;
using Fleet.Ui;

namespace Fleet.Tests.Features.Todos;

public sealed class OpenBoardTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly string _home = Path.Combine(Path.GetTempPath(), "fleet-board-" + Guid.NewGuid().ToString("N"));

    private readonly FileTodoStore _store = new();

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    private BoardProject Project(string name)
    {
        var root = Path.Combine(_home, name);
        Directory.CreateDirectory(root);

        return new BoardProject(name, root);
    }

    private BoardProject WithTicket(string name)
    {
        var project = Project(name);
        var added = _store.Add(project.Root, new Ticket(string.Empty, "idea", null, TicketStatus.Open, TicketSource.Local, Created, "idea"));
        Assert.True(added.Succeeded, added.Error);

        return project;
    }

    private static FleetList Opened(BoardScope scope, BoardTickets board)
    {
        var list = new FleetList();
        var tickets = board.List(scope.Projects);

        FleetRows.Fill(list, BoardRows.For(tickets, scope.ShowsProject), BoardRows.IndexOf(tickets, null, 0));

        return list;
    }

    [Fact]
    public void A_project_with_tickets_opens_with_the_first_ticket_selected()
    {
        var project = WithTicket("alpha");

        var list = Opened(BoardScope.One(project.Name, project.Root), new BoardTickets(_store));

        Assert.Equal(0, FleetRows.Selected(list));
        Assert.Equal(1, list.Source!.Count);
    }

    [Fact]
    public void A_project_without_a_todos_folder_opens_an_empty_board()
    {
        var project = Project("bare");

        var list = Opened(BoardScope.One(project.Name, project.Root), new BoardTickets(_store));

        Assert.Null(list.SelectedItem);
    }

    [Fact]
    public void The_all_projects_board_opens_when_a_project_root_is_missing()
    {
        var missing = new BoardProject("gone", Path.Combine(_home, "gone"));

        var list = Opened(BoardScope.All([missing, Project("bare")]), new BoardTickets(_store));

        Assert.Null(list.SelectedItem);
    }

    [Fact]
    public void The_all_projects_board_shows_the_readable_tickets_beside_a_missing_root()
    {
        var missing = new BoardProject("gone", Path.Combine(_home, "gone"));

        var list = Opened(BoardScope.All([missing, WithTicket("alpha")]), new BoardTickets(_store));

        Assert.Equal(1, list.Source!.Count);
        Assert.Equal(0, FleetRows.Selected(list));
    }
}
