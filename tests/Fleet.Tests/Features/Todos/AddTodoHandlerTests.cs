using Fleet.Features.Todos.AddTodo;
using Fleet.Features.Todos.AddTodo.Models;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Features.Todos;

public class AddTodoHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTodoStore _store = new();

    private AddTodoHandler Handler => new(_store);

    [Fact]
    public void The_title_is_the_first_non_empty_line_and_the_body_the_full_text()
    {
        var added = Handler.Handle(new AddTodoCommand("/p", "\n\n  Add oauth login  \nwith google and github\n"), Now);

        Assert.True(added.Succeeded, added.Error);
        var ticket = Assert.Single(_store.Added);
        Assert.Equal("Add oauth login", ticket.Title);
        Assert.Equal("Add oauth login  \nwith google and github\n", ticket.Body);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(TicketSource.Local, ticket.Source);
        Assert.Null(ticket.Effort);
        Assert.Equal(Now, ticket.Created);
        Assert.Equal("0001-x", added.Value.Id);
    }

    [Fact]
    public void A_long_first_line_is_cut_to_eighty_characters_for_the_title()
    {
        var line = new string('a', 120);

        Handler.Handle(new AddTodoCommand("/p", line), Now);

        Assert.Equal(new string('a', 80), _store.Added.Single().Title);
        Assert.Equal(line + "\n", _store.Added.Single().Body);
    }

    [Fact]
    public void An_explicit_title_and_effort_win()
    {
        Handler.Handle(new AddTodoCommand("/p", "details here", "Short title", TicketEffort.L), Now);

        var ticket = _store.Added.Single();
        Assert.Equal("Short title", ticket.Title);
        Assert.Equal("details here\n", ticket.Body);
        Assert.Equal(TicketEffort.L, ticket.Effort);
    }

    [Theory]
    [InlineData("xl", TicketEffort.XL)]
    [InlineData(" S ", TicketEffort.S)]
    [InlineData("", null)]
    public void A_tool_call_reads_its_effort_from_text(string effort, TicketEffort? expected)
    {
        var added = Handler.HandleTool("/p", "Title", "body", effort, Now);

        Assert.True(added.Succeeded, added.Error);
        Assert.Equal(expected, _store.Added.Single().Effort);
        Assert.Equal("Title", _store.Added.Single().Title);
    }

    [Fact]
    public void A_tool_call_with_an_unknown_effort_is_refused()
    {
        var added = Handler.HandleTool("/p", "Title", string.Empty, "huge", Now);

        Assert.False(added.Succeeded);
        Assert.Contains("XS, S, M, L or XL", added.Error);
        Assert.Empty(_store.Added);
    }

    [Fact]
    public void A_title_without_a_body_leaves_the_body_empty()
    {
        Handler.Handle(new AddTodoCommand("/p", string.Empty, "Only a title"), Now);

        Assert.Equal(string.Empty, _store.Added.Single().Body);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   \n  ", " ")]
    public void Nothing_to_capture_is_refused(string text, string title)
    {
        var added = Handler.Handle(new AddTodoCommand("/p", text, title), Now);

        Assert.False(added.Succeeded);
        Assert.Empty(_store.Added);
    }

    [Fact]
    public void A_store_failure_is_passed_on()
    {
        _store.Failure = "disk full";

        var added = Handler.Handle(new AddTodoCommand("/p", "idea"), Now);

        Assert.Equal("disk full", added.Error);
    }

    private sealed class FakeTodoStore : ITodoStore
    {
        public List<Ticket> Added { get; } = [];

        public string? Failure { get; set; }

        public IReadOnlyList<Ticket> List(string projectRoot) => Added;

        public Ticket? Get(string projectRoot, string id) => Added.FirstOrDefault(t => t.Id == id);

        public Result<Ticket> Add(string projectRoot, Ticket draft)
        {
            if (Failure is { } failure)
            {
                return Result<Ticket>.Fail(failure);
            }

            var ticket = draft with { Id = "0001-x" };
            Added.Add(ticket);
            return Result<Ticket>.Ok(ticket);
        }

        public Result Save(string projectRoot, Ticket ticket) => Result.Ok();

        public Ticket? FindByOrchestration(string projectRoot, string orchestration) => null;
    }
}
