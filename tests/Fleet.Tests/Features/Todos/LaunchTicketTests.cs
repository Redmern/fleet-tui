using Fleet.Features.Todos.LaunchTicket;
using Fleet.Ports.Orchestrations;
using Fleet.Ports.Todos;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Features.Todos;

public class LaunchTicketTests
{
    private const string Root = "/p";

    private static readonly DateTimeOffset Day = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly FakeTodoStore _store = new();

    private readonly FakeDispatcher _dispatcher = new();

    private LaunchTicketHandler Launcher => new(_store, _dispatcher);

    private TicketFollowUp FollowUp => new(_store);

    private static Ticket Ticket(string id, TicketStatus status = TicketStatus.Open, string body = "fix the login page\n\nit 500s\n") =>
        new(id, "fix the login page", null, status, TicketSource.Local, Day, body);

    [Fact]
    public async Task Launching_an_open_ticket_dispatches_its_body_and_marks_it_in_progress_with_the_slug()
    {
        _store.Put(Ticket("0001-login"));
        _dispatcher.Slug = "fix-login";

        var result = await Launcher.HandleAsync("techweb", Root, "0001-login");

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal([("techweb", Root, "fix the login page\n\nit 500s\n")], _dispatcher.Calls);

        var saved = _store.Get(Root, "0001-login")!;
        Assert.Equal(TicketStatus.InProgress, saved.Status);
        Assert.Equal("fix-login", saved.Orchestration);
    }

    [Fact]
    public async Task A_relaunch_clears_the_note_an_earlier_failure_left()
    {
        _store.Put(Ticket("0001-login") with { Note = "old-slug failed" });

        await Launcher.HandleAsync("techweb", Root, "0001-login");

        Assert.Equal(string.Empty, _store.Get(Root, "0001-login")!.Note);
    }

    [Fact]
    public async Task A_ticket_with_an_empty_body_is_dispatched_with_its_title()
    {
        _store.Put(Ticket("0001-login", body: "  \n"));

        await Launcher.HandleAsync("techweb", Root, "0001-login");

        Assert.Equal("fix the login page", _dispatcher.Calls.Single().Prompt);
    }

    [Theory]
    [InlineData(TicketStatus.InProgress, "in-progress")]
    [InlineData(TicketStatus.Done, "done")]
    [InlineData(TicketStatus.Closed, "closed")]
    public async Task A_ticket_that_is_not_open_is_not_dispatched_and_the_reason_is_given(TicketStatus status, string word)
    {
        _store.Put(Ticket("0001-login", status));

        var result = await Launcher.HandleAsync("techweb", Root, "0001-login");

        Assert.False(result.Succeeded);
        Assert.Contains(word, result.Error);
        Assert.Contains("only open tickets", result.Error);
        Assert.Empty(_dispatcher.Calls);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task A_missing_ticket_is_not_dispatched()
    {
        var result = await Launcher.HandleAsync("techweb", Root, "0009-gone");

        Assert.False(result.Succeeded);
        Assert.Empty(_dispatcher.Calls);
    }

    [Fact]
    public async Task When_the_dispatch_fails_the_ticket_is_left_untouched_and_the_error_is_returned()
    {
        _store.Put(Ticket("0001-login"));
        _dispatcher.Failure = "the embedded multiplexer did not respond.";

        var result = await Launcher.HandleAsync("techweb", Root, "0001-login");

        Assert.False(result.Succeeded);
        Assert.Equal("the embedded multiplexer did not respond.", result.Error);
        Assert.Empty(_store.Saved);
        Assert.Equal(TicketStatus.Open, _store.Get(Root, "0001-login")!.Status);
    }

    [Fact]
    public async Task When_the_dispatch_throws_the_ticket_is_left_untouched()
    {
        _store.Put(Ticket("0001-login"));
        _dispatcher.Throws = new IOException("disk full");

        var result = await Launcher.HandleAsync("techweb", Root, "0001-login");

        Assert.False(result.Succeeded);
        Assert.Contains("disk full", result.Error);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task An_edit_made_in_nvim_during_the_dispatch_survives_the_status_write()
    {
        _store.Put(Ticket("0001-login"));
        _dispatcher.During = () => _store.Put(Ticket("0001-login") with { Effort = TicketEffort.M });

        await Launcher.HandleAsync("techweb", Root, "0001-login");

        var saved = _store.Get(Root, "0001-login")!;
        Assert.Equal(TicketEffort.M, saved.Effort);
        Assert.Equal(TicketStatus.InProgress, saved.Status);
    }

    [Fact]
    public void When_the_launched_orchestration_reports_done_the_ticket_is_done()
    {
        _store.Put(Ticket("0001-login", TicketStatus.InProgress) with { Orchestration = "fix-login" });

        FollowUp.Reported(Root, "fix-login", OrchestrationStatus.Done, "merged");

        Assert.Equal(TicketStatus.Done, _store.Get(Root, "0001-login")!.Status);
    }

    [Fact]
    public void When_the_launched_orchestration_reports_failed_the_ticket_reopens_with_a_note()
    {
        _store.Put(Ticket("0001-login", TicketStatus.InProgress) with { Orchestration = "fix-login" });

        FollowUp.Reported(Root, "fix-login", OrchestrationStatus.Failed, "tests would not pass");

        var saved = _store.Get(Root, "0001-login")!;
        Assert.Equal(TicketStatus.Open, saved.Status);
        Assert.Contains("fix-login", saved.Note);
        Assert.Contains("tests would not pass", saved.Note);
    }

    [Fact]
    public void A_failure_without_a_summary_still_leaves_a_note()
    {
        _store.Put(Ticket("0001-login", TicketStatus.InProgress) with { Orchestration = "fix-login" });

        FollowUp.Reported(Root, "fix-login", OrchestrationStatus.Failed, "  ");

        Assert.Equal("fix-login failed", _store.Get(Root, "0001-login")!.Note);
    }

    [Fact]
    public void A_working_report_leaves_the_ticket_alone()
    {
        _store.Put(Ticket("0001-login", TicketStatus.InProgress) with { Orchestration = "fix-login" });

        FollowUp.Reported(Root, "fix-login", OrchestrationStatus.Working, "halfway");

        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void A_report_for_a_ticket_the_user_closed_meanwhile_leaves_it_closed()
    {
        _store.Put(Ticket("0001-login", TicketStatus.Closed) with { Orchestration = "fix-login" });

        FollowUp.Reported(Root, "fix-login", OrchestrationStatus.Done, string.Empty);

        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void A_report_from_an_orchestration_no_ticket_launched_changes_nothing()
    {
        _store.Put(Ticket("0001-login", TicketStatus.InProgress) with { Orchestration = "fix-login" });

        FollowUp.Reported(Root, "something-else", OrchestrationStatus.Done, string.Empty);

        Assert.Empty(_store.Saved);
    }

    [Fact]
    public void A_store_that_throws_does_not_break_the_report()
    {
        _store.Throws = new IOException("locked");

        FollowUp.Reported(Root, "fix-login", OrchestrationStatus.Done, string.Empty);
    }

    private sealed class FakeDispatcher : IDispatcher
    {
        public List<(string Project, string Root, string Prompt)> Calls { get; } = [];

        public string Slug { get; set; } = "a-slug";

        public string? Failure { get; set; }

        public Exception? Throws { get; set; }

        public Action? During { get; set; }

        public Task<Result<string>> DispatchAsync(
            string project, string projectRoot, string prompt, CancellationToken ct = default)
        {
            Calls.Add((project, projectRoot, prompt));
            During?.Invoke();

            if (Throws is { } thrown)
            {
                throw thrown;
            }

            return Task.FromResult(Failure is { } failure ? Result<string>.Fail(failure) : Result<string>.Ok(Slug));
        }
    }

    private sealed class FakeTodoStore : ITodoStore
    {
        private readonly Dictionary<string, Ticket> _tickets = [];

        public List<Ticket> Saved { get; } = [];

        public Exception? Throws { get; set; }

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
            Saved.Add(ticket);
            _tickets[ticket.Id] = ticket;
            return Result.Ok();
        }

        public Ticket? FindByOrchestration(string projectRoot, string orchestration) =>
            Throws is { } thrown
                ? throw thrown
                : _tickets.Values.FirstOrDefault(t => t.Orchestration == orchestration);
    }
}
