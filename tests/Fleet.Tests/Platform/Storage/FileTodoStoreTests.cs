using Fleet.Platform.Storage;
using Fleet.Shared.Todos;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Platform.Storage;

public sealed class FileTodoStoreTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-todos-" + Guid.NewGuid().ToString("N"));

    private readonly FileTodoStore _store = new();

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static Ticket Draft(string title) =>
        new(string.Empty, title, null, TicketStatus.Open, TicketSource.Local, Created, title);

    [Fact]
    public void Adding_a_ticket_writes_a_numbered_markdown_file_under_the_todos_folder()
    {
        var added = _store.Add(_root, Draft("idea"));

        Assert.True(added.Succeeded, added.Error);
        Assert.Equal("0001-idea", added.Value.Id);

        var file = Path.Combine(_root, ".fleet", "todos", "0001-idea.md");
        Assert.True(File.Exists(file));
        Assert.Equal(added.Value, TicketFile.Parse("0001-idea", File.ReadAllText(file)));
    }

    [Fact]
    public void The_next_ticket_takes_one_past_the_highest_counter_on_disk()
    {
        Directory.CreateDirectory(TodoPaths.Folder(_root));
        File.WriteAllText(TodoPaths.File(_root, "0041-older"), "notes");

        Assert.Equal("0042-next", _store.Add(_root, Draft("next")).Value.Id);
    }

    [Fact]
    public void Two_concurrent_updates_of_different_fields_both_survive()
    {
        var id = _store.Add(_root, Draft("shared")).Value.Id;
        const int Rounds = 60;

        static string Next(string counter) => (counter.Length == 0 ? 1 : int.Parse(counter) + 1).ToString();

        Parallel.Invoke(
            () =>
            {
                for (var i = 0; i < Rounds; i++)
                {
                    Assert.True(new FileTodoStore().Update(_root, id, t => t with { Note = Next(t.Note) }).Succeeded);
                }
            },
            () =>
            {
                for (var i = 0; i < Rounds; i++)
                {
                    Assert.True(new FileTodoStore().Update(_root, id, t => t with { Orchestration = Next(t.Orchestration) }).Succeeded);
                }
            });

        var final = _store.Get(_root, id)!;

        Assert.Equal(Rounds.ToString(), final.Note);
        Assert.Equal(Rounds.ToString(), final.Orchestration);
    }

    [Fact]
    public void Updating_a_ticket_that_is_gone_fails_and_writes_nothing()
    {
        var updated = _store.Update(_root, "0009-gone", t => t with { Note = "x" });

        Assert.False(updated.Succeeded);
        Assert.Contains("0009-gone", updated.Error);
        Assert.False(File.Exists(TodoPaths.File(_root, "0009-gone")));
    }

    [Fact]
    public void An_update_that_changes_nothing_does_not_rewrite_the_file()
    {
        var id = _store.Add(_root, Draft("same")).Value.Id;
        var file = TodoPaths.File(_root, id);
        File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var updated = _store.Update(_root, id, t => t);

        Assert.True(updated.Succeeded, updated.Error);
        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), File.GetLastWriteTimeUtc(file));
    }

    [Fact]
    public void Two_tickets_with_the_same_title_get_distinct_files()
    {
        var first = _store.Add(_root, Draft("same idea")).Value;
        var second = _store.Add(_root, Draft("same idea")).Value;

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, Directory.GetFiles(TodoPaths.Folder(_root), "*.md").Length);
    }

    [Fact]
    public void Tickets_added_at_the_same_time_never_share_a_counter()
    {
        Parallel.For(0, 24, i => Assert.True(new FileTodoStore().Add(_root, Draft($"idea {i}")).Succeeded));

        var counters = _store.List(_root).Select(t => TicketName.Counter(t.Id)).ToList();

        Assert.Equal(24, counters.Count);
        Assert.Equal(Enumerable.Range(1, 24).Cast<int?>(), counters.Order());
    }

    [Fact]
    public void Listing_reads_every_ticket_and_keeps_invalid_files_by_their_name()
    {
        _store.Add(_root, Draft("good one"));
        File.WriteAllText(TodoPaths.File(_root, "0002-broken"), "no frontmatter here");
        File.WriteAllText(Path.Combine(TodoPaths.Folder(_root), "README.txt"), "not a ticket");

        var tickets = _store.List(_root);

        Assert.Equal(["0001-good-one", "0002-broken"], tickets.Select(t => t.Id).Order());
        Assert.Equal("0002-broken", tickets.Single(t => t.Id == "0002-broken").Title);
    }

    [Fact]
    public void A_project_without_todos_lists_nothing()
    {
        Assert.Empty(_store.List(_root));
        Assert.Null(_store.Get(_root, "0001-x"));
    }

    [Fact]
    public void Saving_a_ticket_rewrites_its_file_and_it_can_be_found_by_orchestration()
    {
        var ticket = _store.Add(_root, Draft("launch me")).Value;

        var saved = _store.Save(_root, ticket with { Status = TicketStatus.InProgress, Orchestration = "launch-me" });

        Assert.True(saved.Succeeded, saved.Error);
        Assert.Equal(TicketStatus.InProgress, _store.Get(_root, ticket.Id)!.Status);
        Assert.Equal(ticket.Id, _store.FindByOrchestration(_root, "launch-me")!.Id);
        Assert.Null(_store.FindByOrchestration(_root, "other"));
    }
}
