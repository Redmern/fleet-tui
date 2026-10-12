using Fleet.Features.Todos.ShowBoard;
using Fleet.Features.Todos.ShowBoard.Models;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Features.Todos;

public class BoardScopesTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly BoardProject Alpha = new("alpha", "/a");

    private static readonly BoardProject Beta = new("beta", "/b");

    private static readonly BoardScopes Scopes = new(BoardScope.One("alpha", "/a"), BoardScope.All([Beta, Alpha]));

    private static BoardEntry Entry(BoardProject project, string id) =>
        new(project, new Ticket(id, id, null, TicketStatus.Open, TicketSource.Local, Day, string.Empty));

    [Fact]
    public void The_board_opens_on_the_current_project_only()
    {
        Assert.False(Scopes.ShowsAll);
        Assert.Equal([Alpha], Scopes.Current.Projects);
        Assert.Equal(BoardRows.Title("alpha"), Scopes.Current.Title);
        Assert.False(Scopes.Current.ShowsProject);
    }

    [Fact]
    public void Switching_shows_every_project_with_its_name_and_the_all_projects_title()
    {
        var all = Scopes.Switched();

        Assert.True(all.ShowsAll);
        Assert.Equal([Alpha, Beta], all.Current.Projects);
        Assert.Equal(BoardScope.AllTitle, all.Current.Title);
        Assert.True(all.Current.ShowsProject);
    }

    [Fact]
    public void Switching_twice_returns_to_the_project()
    {
        Assert.Equal(Scopes.Current, Scopes.Switched().Switched().Current);
    }

    [Fact]
    public void The_project_name_column_shows_only_in_all_projects_scope()
    {
        IReadOnlyList<BoardEntry> entries = [Entry(Alpha, "0001-a")];

        string Text(BoardScopes scopes) => BoardRows.For(entries, scopes.Current.ShowsProject)[0].Text;

        Assert.DoesNotContain("alpha", Text(Scopes));
        Assert.StartsWith("alpha", Text(Scopes.Switched()));
    }

    [Fact]
    public void A_selected_ticket_that_is_in_the_new_scope_stays_selected()
    {
        IReadOnlyList<BoardEntry> all = [Entry(Beta, "0001-b"), Entry(Alpha, "0002-a")];

        Assert.Equal(1, BoardRows.IndexOf(all, Entry(Alpha, "0002-a"), BoardRows.FirstRow));
    }

    [Fact]
    public void A_selected_ticket_missing_from_the_new_scope_moves_the_cursor_to_the_first_row()
    {
        IReadOnlyList<BoardEntry> project = [Entry(Alpha, "0001-a"), Entry(Alpha, "0002-a")];

        Assert.Equal(0, BoardRows.IndexOf(project, Entry(Beta, "0009-b"), BoardRows.FirstRow));
    }

    [Fact]
    public void Jira_pulls_only_the_projects_in_the_active_scope()
    {
        IReadOnlyList<BoardJira> jira =
        [
            new(Alpha, _ => Task.FromResult(Result<int>.Ok(1))),
            new(Beta, _ => Task.FromResult(Result<int>.Ok(2))),
        ];

        Assert.Equal([Alpha], BoardPull.In(jira, Scopes.Current).Select(j => j.Project));
        Assert.Equal([Alpha, Beta], BoardPull.In(jira, Scopes.Switched().Current).Select(j => j.Project));
    }
}
