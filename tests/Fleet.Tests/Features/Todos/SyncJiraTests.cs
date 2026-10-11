using Fleet.Features.Todos.SyncJira;
using Fleet.Ports.Jira;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Features.Todos;

public class SyncJiraTests
{
    private const string Root = "/p";

    private static readonly DateTimeOffset Day = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly JiraSettings Settings = new("https://acme.atlassian.net", "me@acme.test", "project = WEB", "customfield_10016");

    private readonly FakeTodoStore _store = new();

    private readonly FakeTokens _tokens = new();

    private readonly FakeJiraClient _client = new();

    private int _clientsMade;

    private SyncJiraHandler Sync => new(_store, _tokens, () =>
    {
        _clientsMade++;
        return _client;
    }, new FixedClock(Day));

    private async Task<Result<int>> Pull()
    {
        var result = await Sync.HandleAsync("techweb", Root, Settings);

        Assert.NotNull(result);
        return result.Value;
    }

    private static JiraIssue Issue(string key, string summary = "Fix login", string description = "it 500s", double? points = 3, bool done = false) =>
        new(key, summary, description, points, done);

    [Fact]
    public async Task Without_jira_settings_nothing_runs_and_no_client_is_made()
    {
        var result = await Sync.HandleAsync("techweb", Root, null);

        Assert.Null(result);
        Assert.Equal(0, _clientsMade);
        Assert.Equal(0, _tokens.Reads);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task The_query_runs_with_the_settings_and_the_project_token()
    {
        _tokens.Token = "s3cret";

        await Pull();

        Assert.Equal([(Settings, "s3cret")], _client.Searches);
        Assert.Equal(["techweb"], _tokens.Projects);
    }

    [Fact]
    public async Task A_first_pull_creates_a_jira_ticket_named_after_the_key()
    {
        _client.Issues = [Issue("WEB-12", points: 5)];

        var result = await Pull();

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(1, result.Value);

        var ticket = _store.Get(Root, "WEB-12")!;
        Assert.Equal("Fix login", ticket.Title);
        Assert.Equal(TicketSource.Jira, ticket.Source);
        Assert.Equal("WEB-12", ticket.Jira);
        Assert.Equal(TicketEffort.L, ticket.Effort);
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(Day, ticket.Created);
        Assert.Equal("<!-- jira:start -->\nit 500s\n<!-- jira:end -->\n", ticket.Body);
    }

    [Fact]
    public async Task An_issue_without_story_points_gets_no_effort()
    {
        _client.Issues = [Issue("WEB-12", points: null)];

        await Pull();

        Assert.Null(_store.Get(Root, "WEB-12")!.Effort);
    }

    [Fact]
    public async Task A_second_pull_updates_title_and_description_and_keeps_local_fields()
    {
        _store.Put(new Ticket(
            "WEB-12", "Old title", TicketEffort.XS, TicketStatus.InProgress, TicketSource.Jira, Day.AddDays(-3),
            "<!-- jira:start -->\nold text\n<!-- jira:end -->\n\nmy notes\n")
        {
            Jira = "WEB-12",
            Note = "ask Bob",
            Orchestration = "fix-login",
        });
        _client.Issues = [Issue("WEB-12", summary: "New title", description: "new text", points: 13)];

        await Pull();

        var ticket = _store.Get(Root, "WEB-12")!;
        Assert.Equal("New title", ticket.Title);
        Assert.Equal("<!-- jira:start -->\nnew text\n<!-- jira:end -->\n\nmy notes\n", ticket.Body);
        Assert.Equal(TicketEffort.XS, ticket.Effort);
        Assert.Equal(TicketStatus.InProgress, ticket.Status);
        Assert.Equal("ask Bob", ticket.Note);
        Assert.Equal("fix-login", ticket.Orchestration);
        Assert.Equal(Day.AddDays(-3), ticket.Created);
    }

    [Fact]
    public async Task An_unchanged_issue_does_not_rewrite_its_file()
    {
        _client.Issues = [Issue("WEB-12")];
        await Pull();
        _store.Saved.Clear();

        await Pull();

        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task An_issue_that_left_the_query_is_closed_and_kept()
    {
        _client.Issues = [Issue("WEB-12"), Issue("WEB-13")];
        await Pull();

        _client.Issues = [Issue("WEB-13")];
        await Pull();

        var gone = _store.Get(Root, "WEB-12")!;
        Assert.Equal(TicketStatus.Closed, gone.Status);
        Assert.Equal(SyncJiraHandler.NotInQuery, gone.JiraClosed);
        Assert.Equal(TicketStatus.Open, _store.Get(Root, "WEB-13")!.Status);
    }

    [Fact]
    public async Task Local_tickets_are_never_closed_by_a_pull()
    {
        _store.Put(new Ticket("0001-idea", "idea", null, TicketStatus.Open, TicketSource.Local, Day, "x\n"));

        await Pull();

        Assert.Equal(TicketStatus.Open, _store.Get(Root, "0001-idea")!.Status);
        Assert.Empty(_store.Saved);
    }

    [Fact]
    public async Task An_issue_done_in_jira_is_closed()
    {
        _client.Issues = [Issue("WEB-12")];
        await Pull();

        _client.Issues = [Issue("WEB-12", done: true)];
        await Pull();

        var ticket = _store.Get(Root, "WEB-12")!;
        Assert.Equal(TicketStatus.Closed, ticket.Status);
        Assert.Equal(SyncJiraHandler.ClosedInJira, ticket.JiraClosed);
    }

    [Fact]
    public async Task An_issue_pulled_first_while_done_is_created_closed()
    {
        _client.Issues = [Issue("WEB-12", done: true)];

        await Pull();

        Assert.Equal(TicketStatus.Closed, _store.Get(Root, "WEB-12")!.Status);
    }

    [Fact]
    public async Task An_issue_that_reappears_is_opened_again()
    {
        _client.Issues = [Issue("WEB-12")];
        await Pull();
        _client.Issues = [];
        await Pull();

        _client.Issues = [Issue("WEB-12")];
        await Pull();

        var ticket = _store.Get(Root, "WEB-12")!;
        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(string.Empty, ticket.JiraClosed);
    }

    [Fact]
    public async Task Closing_and_reopening_by_sync_keeps_the_local_note()
    {
        _client.Issues = [Issue("WEB-12")];
        await Pull();
        _store.Put(_store.Get(Root, "WEB-12")! with { Note = "blocked on infra" });

        _client.Issues = [];
        await Pull();
        Assert.Equal("blocked on infra", _store.Get(Root, "WEB-12")!.Note);

        _client.Issues = [Issue("WEB-12")];
        await Pull();
        Assert.Equal("blocked on infra", _store.Get(Root, "WEB-12")!.Note);
        Assert.Equal(TicketStatus.Open, _store.Get(Root, "WEB-12")!.Status);
    }

    [Fact]
    public async Task A_ticket_the_user_closed_stays_closed_while_still_in_the_query()
    {
        _client.Issues = [Issue("WEB-12")];
        await Pull();
        _store.Put(_store.Get(Root, "WEB-12")! with { Status = TicketStatus.Closed });

        await Pull();

        Assert.Equal(TicketStatus.Closed, _store.Get(Root, "WEB-12")!.Status);
    }

    [Fact]
    public async Task Issues_with_keys_that_are_not_plain_file_names_are_skipped()
    {
        _client.Issues = [Issue("../WEB-12"), Issue("WEB-13")];

        var result = await Pull();

        Assert.Equal(1, result.Value);
        Assert.Equal(["WEB-13"], _store.Saved.Select(t => t.Id));
    }

    [Fact]
    public async Task A_ticket_file_that_exists_but_cannot_be_read_is_not_overwritten()
    {
        _store.Unreadable.Add("WEB-12");
        _client.Issues = [Issue("WEB-12"), Issue("WEB-13")];

        var result = await Pull();

        Assert.False(result.Succeeded);
        Assert.Contains("WEB-12", result.Error);
        Assert.Equal(["WEB-13"], _store.Saved.Select(t => t.Id));
    }

    [Fact]
    public async Task A_failed_search_changes_no_ticket_and_returns_the_error()
    {
        _store.Put(new Ticket("WEB-12", "Fix login", null, TicketStatus.Open, TicketSource.Jira, Day, "x\n") { Jira = "WEB-12" });
        _client.Failure = "Jira rejected the credentials (HTTP 401).";

        var result = await Pull();

        Assert.False(result.Succeeded);
        Assert.Equal("Jira rejected the credentials (HTTP 401).", result.Error);
        Assert.Empty(_store.Saved);
        Assert.Equal(TicketStatus.Open, _store.Get(Root, "WEB-12")!.Status);
    }

    [Fact]
    public async Task A_missing_token_fails_before_any_request()
    {
        _tokens.Failure = "no Jira token";

        var result = await Pull();

        Assert.False(result.Succeeded);
        Assert.Equal("no Jira token", result.Error);
        Assert.Empty(_client.Searches);
    }

    [Fact]
    public async Task A_client_that_throws_becomes_a_failed_result_without_the_token()
    {
        _tokens.Token = "s3cret";
        _client.Throws = new HttpRequestException("connection refused");

        var result = await Pull();

        Assert.False(result.Succeeded);
        Assert.Contains("connection refused", result.Error);
        Assert.DoesNotContain("s3cret", result.Error);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeTokens : IJiraTokenStore
    {
        public string Token { get; set; } = "token";

        public string? Failure { get; set; }

        public int Reads => Projects.Count;

        public List<string> Projects { get; } = [];

        public Result<string> Read(string project)
        {
            Projects.Add(project);
            return Failure is { } failure ? Result<string>.Fail(failure) : Result<string>.Ok(Token);
        }
    }

    private sealed class FakeJiraClient : IJiraClient
    {
        public IReadOnlyList<JiraIssue> Issues { get; set; } = [];

        public string? Failure { get; set; }

        public Exception? Throws { get; set; }

        public List<(JiraSettings Settings, string Token)> Searches { get; } = [];

        public Task<Result<IReadOnlyList<JiraIssue>>> SearchAsync(JiraSettings settings, string token, CancellationToken ct = default)
        {
            Searches.Add((settings, token));

            if (Throws is { } thrown)
            {
                throw thrown;
            }

            return Task.FromResult(
                Failure is { } failure
                    ? Result<IReadOnlyList<JiraIssue>>.Fail(failure)
                    : Result<IReadOnlyList<JiraIssue>>.Ok(Issues));
        }
    }

    private sealed class FakeTodoStore : ITodoStore
    {
        private readonly Dictionary<string, Ticket> _tickets = [];

        public List<Ticket> Saved { get; } = [];

        public HashSet<string> Unreadable { get; } = [];

        public bool Exists(string projectRoot, string id) => Unreadable.Contains(id) || _tickets.ContainsKey(id);

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

        public Ticket? FindByOrchestration(string projectRoot, string orchestration) => null;
    }
}
