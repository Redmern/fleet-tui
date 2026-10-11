using Fleet.Shared.Todos;
using Fleet.Shared.Todos.Enums;

namespace Fleet.Tests.Shared.Todos;

public class JiraTicketTests
{
    [Theory]
    [InlineData(0.0, TicketEffort.XS)]
    [InlineData(1.0, TicketEffort.XS)]
    [InlineData(2.0, TicketEffort.S)]
    [InlineData(3.0, TicketEffort.M)]
    [InlineData(4.0, TicketEffort.M)]
    [InlineData(5.0, TicketEffort.L)]
    [InlineData(6.0, TicketEffort.L)]
    [InlineData(7.0, TicketEffort.L)]
    [InlineData(8.0, TicketEffort.XL)]
    [InlineData(13.0, TicketEffort.XL)]
    public void Story_points_map_to_a_t_shirt_size(double points, TicketEffort expected) =>
        Assert.Equal(expected, JiraEffort.From(points));

    [Fact]
    public void No_story_points_leave_the_effort_unset() =>
        Assert.Null(JiraEffort.From(null));

    [Fact]
    public void A_new_body_is_the_jira_section_alone()
    {
        Assert.Equal(
            "<!-- jira:start -->\nfix the login\n<!-- jira:end -->\n",
            JiraSection.Replace(string.Empty, "fix the login\n"));
    }

    [Fact]
    public void Replacing_keeps_local_text_above_and_below_the_section()
    {
        var body = "my intro\n\n<!-- jira:start -->\nold text\n<!-- jira:end -->\n\n## notes\nlocal note\n";

        Assert.Equal(
            "my intro\n\n<!-- jira:start -->\nnew text\n<!-- jira:end -->\n\n## notes\nlocal note\n",
            JiraSection.Replace(body, "new text"));
    }

    [Fact]
    public void A_body_without_markers_gets_the_section_on_top_and_keeps_its_text()
    {
        Assert.Equal(
            "<!-- jira:start -->\nnew text\n<!-- jira:end -->\n\nwritten by hand\n",
            JiraSection.Replace("written by hand\n", "new text"));
    }

    [Fact]
    public void Markers_inside_a_description_cannot_end_the_section_early()
    {
        var once = JiraSection.Replace("local\n", "see <!-- jira:end --> here");
        var twice = JiraSection.Replace(once, "see <!-- jira:end --> here");

        Assert.Equal(once, twice);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(twice, "<!-- jira:end -->"));
    }

    [Fact]
    public void A_start_marker_without_an_end_is_replaced_to_the_end_of_the_body() =>
        Assert.Equal(
            "intro\n<!-- jira:start -->\nnew\n<!-- jira:end -->\n",
            JiraSection.Replace("intro\n<!-- jira:start -->\nold text lost its end\n", "new"));

    [Fact]
    public void An_empty_description_leaves_an_empty_section()
    {
        Assert.Equal("<!-- jira:start -->\n<!-- jira:end -->\n", JiraSection.Replace(string.Empty, "  \n"));
    }

    [Theory]
    [InlineData("WEB-12", true)]
    [InlineData("AB2_C-1", true)]
    [InlineData("web-12", false)]
    [InlineData("../WEB-1", false)]
    [InlineData("WEB-", false)]
    [InlineData("WEB-1/2", false)]
    [InlineData("", false)]
    public void Only_plain_issue_keys_become_ticket_ids(string key, bool valid) =>
        Assert.Equal(valid, JiraSection.IsKey(key));
}
