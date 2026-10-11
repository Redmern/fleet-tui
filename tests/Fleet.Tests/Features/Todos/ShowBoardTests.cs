using Fleet.Features.Todos.ShowBoard;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Features.Todos;

public class ShowBoardTests
{
    private const string Root = "/p";

    private static readonly DateTimeOffset Day = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTodoStore _store = new();

    private BoardTickets Board => new(_store);

    private static Ticket Ticket(
        string id,
        TicketStatus status = TicketStatus.Open,
        int day = 0,
        TicketEffort? effort = null,
        TicketSource source = TicketSource.Local) =>
        new(id, $"title of {id}", effort, status, source, Day.AddDays(day), "body\n");

    [Fact]
    public void Tickets_are_ordered_open_in_progress_done_closed_and_newest_first_within_a_status()
    {
        _store.Put(
            Ticket("0001-old-open", TicketStatus.Open, day: 1),
            Ticket("0002-closed", TicketStatus.Closed, day: 9),
            Ticket("0003-done", TicketStatus.Done, day: 8),
            Ticket("0004-new-open", TicketStatus.Open, day: 5),
            Ticket("0005-busy", TicketStatus.InProgress, day: 7));

        var ids = Board.List(Root).Select(t => t.Id);

        Assert.Equal(["0004-new-open", "0001-old-open", "0005-busy", "0003-done", "0002-closed"], ids);
    }

    [Fact]
    public void A_jira_pull_reports_how_many_issues_it_synced() =>
        Assert.Equal("Jira: synced 3.", BoardRows.JiraSynced(Result<int>.Ok(3)));

    [Fact]
    public void A_failed_jira_pull_shows_its_error_and_that_cached_tickets_are_shown() =>
        Assert.Equal(
            "Jira: Jira is unreachable at https://acme.atlassian.net: refused (showing cached tickets)",
            BoardRows.JiraSynced(Result<int>.Fail("Jira is unreachable at https://acme.atlassian.net: refused")));

    [Fact]
    public async Task A_jira_pull_that_throws_becomes_a_failed_result()
    {
        var result = await BoardRows.SafelyAsync(new Func<Task<Result<int>>>(() => throw new InvalidOperationException("boom")));

        Assert.False(result.Succeeded);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public void A_row_shows_id_title_effort_status_and_source()
    {
        var row = BoardRows.For([Ticket("PROJ-12", TicketStatus.InProgress, effort: TicketEffort.M, source: TicketSource.Jira)])
            .Single()
            .Text;

        Assert.Contains("PROJ-12", row);
        Assert.Contains("title of PROJ-12", row);
        Assert.Contains(" M ", row);
        Assert.Contains("in-progress", row);
        Assert.Contains("jira", row);
    }

    [Fact]
    public void Rows_line_up_their_columns()
    {
        var rows = BoardRows.For([Ticket("0001-a"), Ticket("0002-a-much-longer-id", effort: TicketEffort.XL)]);

        var titleAt = rows.Select(r => r.Text.IndexOf("title of", StringComparison.Ordinal)).Distinct();

        Assert.Single(titleAt);
    }

    [Fact]
    public void A_long_title_is_cut_so_the_row_stays_on_one_line()
    {
        var ticket = Ticket("0001-a") with { Title = new string('t', 200) };

        var row = BoardRows.For([ticket]).Single().Text;

        Assert.DoesNotContain(new string('t', BoardRows.TitleWidth + 1), row);
        Assert.Contains("local", row);
    }

    [Theory]
    [InlineData(null, TicketEffort.XS)]
    [InlineData(TicketEffort.XS, TicketEffort.S)]
    [InlineData(TicketEffort.S, TicketEffort.M)]
    [InlineData(TicketEffort.M, TicketEffort.L)]
    [InlineData(TicketEffort.L, TicketEffort.XL)]
    [InlineData(TicketEffort.XL, null)]
    public void The_effort_key_cycles_through_the_sizes_and_back_to_unset(TicketEffort? from, TicketEffort? to)
    {
        _store.Put(Ticket("0001-a", effort: from));

        var cycled = Board.CycleEffort(Root, "0001-a");

        Assert.True(cycled.Succeeded, cycled.Error);
        Assert.Equal(to, cycled.Value.Effort);
        Assert.Equal(to, _store.Saved.Single().Effort);
    }

    [Fact]
    public void Cycling_reads_the_file_again_so_an_edit_made_meanwhile_is_kept()
    {
        _store.Put(Ticket("0001-a"));
        _store.Put(Ticket("0001-a") with { Title = "edited in nvim", Body = "new body\n" });

        var cycled = Board.CycleEffort(Root, "0001-a");

        Assert.Equal("edited in nvim", cycled.Value.Title);
        Assert.Equal("new body\n", _store.Saved.Single().Body);
    }

    [Fact]
    public async Task An_open_that_throws_becomes_a_failed_result_so_the_board_stays_usable()
    {
        var opened = await BoardRows.SafelyAsync(() => throw new InvalidOperationException("fleetd did not answer"));

        Assert.False(opened.Succeeded);
        Assert.Contains("fleetd did not answer", opened.Error);
    }

    [Fact]
    public async Task An_open_that_fails_async_becomes_a_failed_result()
    {
        var opened = await BoardRows.SafelyAsync(async () =>
        {
            await Task.Yield();
            throw new IOException("pipe closed");
        });

        Assert.Equal("pipe closed", opened.Error);
    }

    [Fact]
    public void The_status_line_names_the_orchestration_a_launch_started()
    {
        var launched = Ticket("0001-a", TicketStatus.InProgress) with { Orchestration = "fix-a" };

        Assert.Equal("Launched 0001-a as sub-orchestrator fix-a.", BoardRows.Launched(launched));
    }

    [Fact]
    public async Task A_launch_that_throws_becomes_a_failed_result()
    {
        var launched = await BoardRows.SafelyAsync<Ticket>(() => throw new IOException("locked"));

        Assert.Equal("locked", launched.Error);
    }

    [Fact]
    public void The_status_line_names_the_saved_effort()
    {
        Assert.Equal("Effort of 0001-a is M.", BoardRows.EffortSaved(Ticket("0001-a", effort: TicketEffort.M)));
        Assert.Equal("Effort of 0001-a is unset.", BoardRows.EffortSaved(Ticket("0001-a")));
    }

    [Fact]
    public void Closing_sets_the_status_closed_and_keeps_the_ticket()
    {
        _store.Put(Ticket("0001-a", TicketStatus.InProgress));

        var closed = Board.Close(Root, "0001-a");

        Assert.True(closed.Succeeded, closed.Error);
        Assert.Equal(TicketStatus.Closed, _store.Saved.Single().Status);
        Assert.Single(Board.List(Root));
    }

    [Fact]
    public void A_ticket_that_is_gone_is_reported_not_thrown()
    {
        var cycled = Board.CycleEffort(Root, "0009-gone");
        var closed = Board.Close(Root, "0009-gone");

        Assert.False(cycled.Succeeded);
        Assert.Contains("0009-gone", cycled.Error);
        Assert.False(closed.Succeeded);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void A_failed_save_is_reported()
    {
        _store.Put(Ticket("0001-a"));
        _store.Failure = "read-only file";

        var closed = Board.Close(Root, "0001-a");

        Assert.Equal("read-only file", closed.Error);
    }

    [Fact]
    public void A_change_on_disk_shows_on_the_next_list()
    {
        _store.Put(Ticket("0001-a"));
        var before = BoardRows.For(Board.List(Root));

        _store.Put(Ticket("0001-a") with { Title = "renamed on disk" });
        var after = BoardRows.For(Board.List(Root));

        Assert.DoesNotContain("renamed on disk", before.Single().Text);
        Assert.Contains("renamed on disk", after.Single().Text);
    }

    [Fact]
    public void The_selection_follows_a_ticket_when_the_order_changes()
    {
        var tickets = new[] { Ticket("0002-b", day: 2), Ticket("0001-a", day: 1) };

        Assert.Equal(1, BoardRows.IndexOf(tickets, "0001-a", fallback: 0));
        Assert.Equal(1, BoardRows.IndexOf(tickets, "0009-gone", fallback: 7));
        Assert.Equal(0, BoardRows.IndexOf([], "0001-a", fallback: 3));
    }

    private sealed class FakeTodoStore : ITodoStore
    {
        private readonly Dictionary<string, Ticket> _tickets = [];

        public List<Ticket> Saved { get; } = [];

        public string? Failure { get; set; }

        public void Put(params Ticket[] tickets)
        {
            foreach (var ticket in tickets)
            {
                _tickets[ticket.Id] = ticket;
            }
        }

        public IReadOnlyList<Ticket> List(string projectRoot) => [.. _tickets.Values];

        public Ticket? Get(string projectRoot, string id) => _tickets.GetValueOrDefault(id);

        public Result<Ticket> Add(string projectRoot, Ticket draft) => Result<Ticket>.Fail("not used");

        public Result Save(string projectRoot, Ticket ticket)
        {
            if (Failure is { } failure)
            {
                return Result.Fail(failure);
            }

            Saved.Add(ticket);
            _tickets[ticket.Id] = ticket;
            return Result.Ok();
        }

        public Ticket? FindByOrchestration(string projectRoot, string orchestration) => null;
    }
}
