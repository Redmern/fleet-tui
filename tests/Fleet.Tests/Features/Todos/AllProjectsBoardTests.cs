using Fleet.Features.Todos.ShowBoard;
using Fleet.Features.Todos.ShowBoard.Models;
using Fleet.Platform.Storage;
using Fleet.Ports.Todos;
using Fleet.Shared.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Features.Todos;

public class AllProjectsBoardTests
{
    private static readonly DateTimeOffset Day = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly BoardProject Alpha = new("alpha", "/a");

    private static readonly BoardProject Beta = new("beta", "/b");

    private readonly RootedTodoStore _store = new();

    private BoardTickets Board => new(_store);

    private static Ticket Ticket(string id, TicketStatus status = TicketStatus.Open, int day = 0) =>
        new(id, $"title of {id}", null, status, TicketSource.Local, Day.AddDays(day), "body\n");

    [Fact]
    public void The_single_project_board_keeps_its_title_and_shows_no_project_column()
    {
        var scope = BoardScope.One("alpha", "/a");

        Assert.Equal(BoardRows.Title("alpha"), scope.Title);
        Assert.False(scope.ShowsProject);
        Assert.Equal([Alpha], scope.Projects);
    }

    [Fact]
    public void The_all_projects_board_lists_every_project_by_name_with_a_project_column()
    {
        var scope = BoardScope.All([Beta, Alpha]);

        Assert.Equal("all projects — todo board", scope.Title);
        Assert.True(scope.ShowsProject);
        Assert.Equal([Alpha, Beta], scope.Projects);
    }

    [Fact]
    public void Tickets_from_every_project_are_listed_together_in_board_order()
    {
        _store.Put("/a", Ticket("0001-old", day: 1), Ticket("0002-closed", TicketStatus.Closed, day: 9));
        _store.Put("/b", Ticket("0001-new", day: 5), Ticket("0002-busy", TicketStatus.InProgress, day: 2));

        var listed = Board.List([Alpha, Beta]).Select(e => $"{e.Project.Name}/{e.Ticket.Id}");

        Assert.Equal(["beta/0001-new", "alpha/0001-old", "beta/0002-busy", "alpha/0002-closed"], listed);
    }

    [Fact]
    public void A_project_whose_tickets_cannot_be_read_does_not_hide_the_others()
    {
        _store.Put("/b", Ticket("0001-b"));
        _store.Broken.Add("/a");

        var listed = Board.List([Alpha, Beta]);

        Assert.Equal("0001-b", Assert.Single(listed).Ticket.Id);
    }

    [Fact]
    public void A_ticket_file_with_broken_frontmatter_still_shows_with_the_others()
    {
        var root = Path.Combine(Path.GetTempPath(), "fleet-board-" + Guid.NewGuid().ToString("N"));
        var store = new FileTodoStore();

        try
        {
            Assert.True(store.Add(root, Ticket("") with { Title = "good one" }).Succeeded);
            var folder = TodoPaths.Folder(root);
            File.WriteAllText(Path.Combine(folder, "0002-no-title.md"), "---\nstatus: open\n---\nbody\n");
            File.WriteAllText(Path.Combine(folder, "0003-unclosed.md"), "---\ntitle: never closed\n");
            File.WriteAllText(Path.Combine(folder, "0004-empty.md"), string.Empty);
            File.WriteAllBytes(Path.Combine(folder, "0005-binary.md"), [0xff, 0xfe, 0x00, 0x2d, 0x2d, 0x2d, 0x0a, 0x3a]);

            var listed = new BoardTickets(store).List([new BoardProject("p", root)]);

            Assert.Equal(
                ["0001-good-one", "0002-no-title", "0003-unclosed", "0004-empty", "0005-binary"],
                listed.Select(e => e.Ticket.Id).Order(StringComparer.Ordinal));
            Assert.Equal("0002-no-title", listed.Single(e => e.Ticket.Id == "0002-no-title").Ticket.Title);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Effort_and_close_act_on_the_tickets_own_project()
    {
        _store.Put("/a", Ticket("0001-same"));
        _store.Put("/b", Ticket("0001-same"));

        Board.CycleEffort("/b", "0001-same");
        Board.Close("/b", "0001-same");

        Assert.Equal(TicketStatus.Open, _store.Get("/a", "0001-same")!.Status);
        Assert.Null(_store.Get("/a", "0001-same")!.Effort);
        Assert.Equal(TicketStatus.Closed, _store.Get("/b", "0001-same")!.Status);
        Assert.Equal(TicketEffort.XS, _store.Get("/b", "0001-same")!.Effort);
    }

    [Fact]
    public void The_all_projects_rows_start_with_the_project_in_one_column()
    {
        var rows = BoardRows.For(
            [new BoardEntry(Alpha, Ticket("0001-a")), new BoardEntry(new("a-longer-name", "/l"), Ticket("0002-b"))],
            showProject: true);

        Assert.StartsWith("alpha ", rows[0].Text);
        Assert.StartsWith("a-longer-name ", rows[1].Text);
        Assert.Single(rows.Select(r => r.Text.IndexOf("000", StringComparison.Ordinal)).Distinct());
    }

    [Fact]
    public void The_single_project_rows_look_as_before()
    {
        var ticket = Ticket("0001-a");

        var row = BoardRows.For([new BoardEntry(Alpha, ticket)], showProject: false).Single().Text;

        Assert.Equal(BoardRows.For([ticket]).Single().Text, row);
    }

    [Fact]
    public void The_selection_follows_a_ticket_by_project_and_id()
    {
        var entries = new[]
        {
            new BoardEntry(Alpha, Ticket("0001-same")),
            new BoardEntry(Beta, Ticket("0001-same")),
        };

        Assert.Equal(1, BoardRows.IndexOf(entries, entries[1], fallback: 0));
        Assert.Equal(0, BoardRows.IndexOf(entries, new BoardEntry(Beta, Ticket("0009-gone")), fallback: 0));
        Assert.Equal(0, BoardRows.IndexOf([], entries[0], fallback: 4));
    }

    [Fact]
    public async Task Jira_pulls_run_one_project_after_the_other()
    {
        var log = new List<string>();

        BoardJira Pull(BoardProject project, int count) => new(project, async _ =>
        {
            log.Add($"start {project.Name}");
            await Task.Yield();
            log.Add($"end {project.Name}");
            return Result<int>.Ok(count);
        });

        var pulled = await BoardPull.AllAsync([Pull(Alpha, 2), Pull(Beta, 3)], CancellationToken.None);

        Assert.Equal(["start alpha", "end alpha", "start beta", "end beta"], log);
        Assert.Equal("Jira: alpha synced 2; beta synced 3.", BoardRows.JiraSynced(pulled, named: true));
    }

    [Fact]
    public async Task A_failing_jira_project_shows_its_error_and_the_next_project_still_pulls()
    {
        var pulled = await BoardPull.AllAsync(
            [
                new BoardJira(Alpha, _ => throw new HttpRequestException("refused")),
                new BoardJira(Beta, _ => Task.FromResult(Result<int>.Ok(1))),
            ],
            CancellationToken.None);

        Assert.Equal("Jira: alpha: refused; beta synced 1 (showing cached tickets for alpha).", BoardRows.JiraSynced(pulled, named: true));
    }

    [Fact]
    public async Task A_closed_board_stops_pulling_the_remaining_projects()
    {
        using var stop = new CancellationTokenSource();
        var betaPulled = false;

        await BoardPull.AllAsync(
            [
                new BoardJira(Alpha, _ =>
                {
                    stop.Cancel();
                    return Task.FromResult(Result<int>.Ok(0));
                }),
                new BoardJira(Beta, _ =>
                {
                    betaPulled = true;
                    return Task.FromResult(Result<int>.Ok(0));
                }),
            ],
            stop.Token);

        Assert.False(betaPulled);
    }

    [Fact]
    public void A_single_jira_project_keeps_the_single_board_wording()
    {
        Assert.Equal("Jira: synced 3.", BoardRows.JiraSynced([("alpha", Result<int>.Ok(3))], named: false));
        Assert.Equal(
            "Jira: down (showing cached tickets)",
            BoardRows.JiraSynced([("alpha", Result<int>.Fail("down"))], named: false));
    }

    [Fact]
    public void The_all_projects_board_names_the_project_even_when_only_one_has_jira() =>
        Assert.Equal(
            "Jira: alpha: down (showing cached tickets for alpha).",
            BoardRows.JiraSynced([("alpha", Result<int>.Fail("down"))], named: true));

    private sealed class RootedTodoStore : ITodoStore
    {
        private readonly Dictionary<(string Root, string Id), Ticket> _tickets = [];

        public HashSet<string> Broken { get; } = [];

        public void Put(string root, params Ticket[] tickets)
        {
            foreach (var ticket in tickets)
            {
                _tickets[(root, ticket.Id)] = ticket;
            }
        }

        public IReadOnlyList<Ticket> List(string projectRoot) =>
            Broken.Contains(projectRoot)
                ? throw new UnauthorizedAccessException(projectRoot)
                : [.. _tickets.Where(t => t.Key.Root == projectRoot).Select(t => t.Value)];

        public Ticket? Get(string projectRoot, string id) => _tickets.GetValueOrDefault((projectRoot, id));

        public Result<Ticket> Add(string projectRoot, Ticket draft) => Result<Ticket>.Fail("not used");

        public Result Save(string projectRoot, Ticket ticket)
        {
            _tickets[(projectRoot, ticket.Id)] = ticket;
            return Result.Ok();
        }

        public Ticket? FindByOrchestration(string projectRoot, string orchestration) => null;
    }
}
