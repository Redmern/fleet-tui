using Fleet.Shared.Todos;
using Fleet.Shared.Todos.Enums;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Shared.Todos;

public class TicketFileTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 11, 9, 30, 0, TimeSpan.Zero);

    [Fact]
    public void A_ticket_round_trips_through_its_file_text()
    {
        var ticket = new Ticket(
            "0007-add-oauth", "Add \"oauth\": login", TicketEffort.M, TicketStatus.InProgress, TicketSource.Local, Created,
            "Add oauth login\n\n- google\n- github\n")
        {
            Orchestration = "add-oauth",
            Note = "failed once",
        };

        var parsed = TicketFile.Parse("0007-add-oauth", TicketFile.Format(ticket));

        Assert.Equal(ticket, parsed);
    }

    [Fact]
    public void An_unset_effort_and_empty_optional_fields_round_trip()
    {
        var ticket = new Ticket("0001-idea", "idea", null, TicketStatus.Open, TicketSource.Local, Created, "idea");

        var text = TicketFile.Format(ticket);
        var parsed = TicketFile.Parse("0001-idea", text);

        Assert.Equal(ticket, parsed);
        Assert.StartsWith("---\n", text);
        Assert.Contains("\nstatus: open\n", text);
        Assert.Contains("\nsource: local\n", text);
        Assert.DoesNotContain("orchestration:", text);
    }

    [Fact]
    public void A_jira_ticket_keeps_its_key()
    {
        var ticket = new Ticket("ABC-12", "Fix login", TicketEffort.XL, TicketStatus.Closed, TicketSource.Jira, Created, string.Empty)
        {
            Jira = "ABC-12",
        };

        Assert.Equal(ticket, TicketFile.Parse("ABC-12", TicketFile.Format(ticket)));
    }

    [Fact]
    public void Frontmatter_written_by_hand_is_read()
    {
        const string text = "---\r\ntitle: Ship it\r\neffort: s\r\nstatus: done\r\nsource: local\r\ncreated: 2026-10-01\r\n---\r\nbody line\r\n";

        var parsed = TicketFile.Parse("0003-ship-it", text);

        Assert.Equal("0003-ship-it", parsed.Id);
        Assert.Equal("Ship it", parsed.Title);
        Assert.Equal(TicketEffort.S, parsed.Effort);
        Assert.Equal(TicketStatus.Done, parsed.Status);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), parsed.Created);
        Assert.Equal("body line\r\n", parsed.Body);
    }

    [Theory]
    [InlineData("")]
    [InlineData("just some notes\nwithout frontmatter")]
    [InlineData("---\ntitle: never closed\n")]
    [InlineData("---\neffort: M\nstatus: open\n---\nno title\n")]
    [InlineData("---\nthis is not a field\n---\n")]
    public void Invalid_frontmatter_falls_back_to_the_file_name_as_title(string text)
    {
        var parsed = TicketFile.Parse("0009-broken", text);

        Assert.Equal("0009-broken", parsed.Id);
        Assert.Equal("0009-broken", parsed.Title);
        Assert.Equal(TicketStatus.Open, parsed.Status);
        Assert.False(TicketFile.IsValid(text));
    }

    [Fact]
    public void Extra_hand_written_fields_comments_and_lists_do_not_invalidate_the_ticket()
    {
        const string text = "---\n# mine\ntitle: 'Kept'\nstory_points: 3\ntags:\n  - a\n  - b\nstatus: done\n---\nbody\n";

        var parsed = TicketFile.Parse("0004-kept", text);

        Assert.True(TicketFile.IsValid(text));
        Assert.Equal("Kept", parsed.Title);
        Assert.Equal(TicketStatus.Done, parsed.Status);
    }

    [Fact]
    public void Unknown_values_fall_back_without_failing()
    {
        var parsed = TicketFile.Parse("0002-x", "---\ntitle: x\neffort: huge\nstatus: someday\nsource: email\ncreated: soon\n---\n");

        Assert.Equal("x", parsed.Title);
        Assert.Null(parsed.Effort);
        Assert.Equal(TicketStatus.Open, parsed.Status);
        Assert.Equal(TicketSource.Local, parsed.Source);
    }

    [Fact]
    public void A_multi_line_note_is_kept_on_one_line()
    {
        var ticket = new Ticket("0001-a", "a", null, TicketStatus.Open, TicketSource.Local, Created, string.Empty)
        {
            Note = "first\nsecond",
        };

        Assert.Equal("first second", TicketFile.Parse("0001-a", TicketFile.Format(ticket)).Note);
    }

    [Fact]
    public void The_jira_closed_marker_round_trips()
    {
        var ticket = new Ticket("WEB-1", "t", null, TicketStatus.Closed, TicketSource.Jira, new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), "b\n")
        {
            Jira = "WEB-1",
            JiraClosed = "query",
        };

        var text = TicketFile.Format(ticket);

        Assert.Contains("jira-closed: query", text);
        Assert.Equal(ticket, TicketFile.Parse("WEB-1", text));
    }
}
