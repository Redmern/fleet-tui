using Fleet.Ports.Jira;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;
using Fleet.Shared.Todos;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Features.Todos.SyncJira;

public sealed class SyncJiraHandler(
    ITodoStore store, IJiraTokenStore tokens, Func<IJiraClient> client, TimeProvider? clock = null)
{
    public const string ClosedInJira = "done";

    public const string NotInQuery = "query";

    public async Task<Result<int>?> HandleAsync(
        string project, string projectRoot, JiraSettings? settings, CancellationToken ct = default)
    {
        if (settings is null)
        {
            return null;
        }

        var token = tokens.Read(project);

        if (!token.Succeeded)
        {
            return Result<int>.Fail(token.Error!);
        }

        Result<IReadOnlyList<JiraIssue>> found;

        try
        {
            found = await client().SearchAsync(settings, token.Value, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Result<int>.Fail($"Jira is unreachable: {e.Message}");
        }

        return found.Succeeded ? Merge(projectRoot, found.Value) : Result<int>.Fail(found.Error!);
    }

    private Result<int> Merge(string projectRoot, IReadOnlyList<JiraIssue> issues)
    {
        var now = (clock ?? TimeProvider.System).GetUtcNow();
        var pulled = issues.Where(i => JiraSection.IsKey(i.Key)).ToList();
        var keys = pulled.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);
        var failures = new List<string>();

        foreach (var issue in pulled)
        {
            var current = store.Get(projectRoot, issue.Key);

            if (current is null && store.Exists(projectRoot, issue.Key))
            {
                failures.Add($"{issue.Key} could not be read, so it was left alone");
                continue;
            }

            Keep(projectRoot, current, current is null ? Created(issue, now) : Updated(current, issue), failures);
        }

        foreach (var listed in store.List(projectRoot).Where(t => IsPulled(t) && !keys.Contains(t.Id)))
        {
            if (store.Get(projectRoot, listed.Id) is { } gone && IsPulled(gone))
            {
                Keep(projectRoot, gone, Closed(gone, NotInQuery), failures);
            }
        }

        return failures.Count == 0
            ? Result<int>.Ok(pulled.Count)
            : Result<int>.Fail($"pulled {pulled.Count} Jira issues, but {string.Join("; ", failures)}");
    }

    private void Keep(string projectRoot, Ticket? current, Ticket next, List<string> failures)
    {
        if (current is not null && TicketFile.Format(current) == TicketFile.Format(next))
        {
            return;
        }

        var saved = store.Save(projectRoot, next);

        if (!saved.Succeeded)
        {
            failures.Add(saved.Error!);
        }
    }

    private static bool IsPulled(Ticket ticket) =>
        ticket.Source == TicketSource.Jira && ticket.Jira.Length > 0 && JiraSection.IsKey(ticket.Id);

    private static Ticket Created(JiraIssue issue, DateTimeOffset now)
    {
        var ticket = new Ticket(
            issue.Key,
            Title(issue),
            JiraEffort.From(issue.StoryPoints),
            TicketStatus.Open,
            TicketSource.Jira,
            now,
            JiraSection.Replace(string.Empty, issue.Description))
        {
            Jira = issue.Key,
        };

        return issue.Done ? Closed(ticket, ClosedInJira) : ticket;
    }

    private static Ticket Updated(Ticket current, JiraIssue issue)
    {
        var ticket = current with
        {
            Title = Title(issue),
            Source = TicketSource.Jira,
            Jira = issue.Key,
            Body = JiraSection.Replace(current.Body, issue.Description),
        };

        if (issue.Done)
        {
            return Closed(ticket, ClosedInJira);
        }

        return ticket.Status == TicketStatus.Closed && ticket.JiraClosed.Length > 0
            ? ticket with { Status = TicketStatus.Open, JiraClosed = string.Empty }
            : ticket with { JiraClosed = string.Empty };
    }

    private static Ticket Closed(Ticket ticket, string why) =>
        ticket.Status == TicketStatus.Closed ? ticket : ticket with { Status = TicketStatus.Closed, JiraClosed = why };

    private static string Title(JiraIssue issue) =>
        string.IsNullOrWhiteSpace(issue.Summary) ? issue.Key : issue.Summary.Trim();
}
