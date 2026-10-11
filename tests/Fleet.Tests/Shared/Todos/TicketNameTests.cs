using Fleet.Shared.Todos;

namespace Fleet.Tests.Shared.Todos;

public class TicketNameTests
{
    [Fact]
    public void A_name_is_a_four_digit_counter_and_a_slug_of_the_title()
    {
        Assert.Equal("0001-add-oauth-login", TicketName.For(1, "Add OAuth login!"));
        Assert.Equal("0042-fix-it", TicketName.For(42, "fix   it"));
    }

    [Fact]
    public void A_title_with_nothing_sluggable_still_gets_a_name()
    {
        Assert.Matches("^0003-[a-z0-9-]+$", TicketName.For(3, "???"));
    }

    [Fact]
    public void The_next_counter_is_one_past_the_highest_existing_counter()
    {
        Assert.Equal(1, TicketName.NextCounter([]));
        Assert.Equal(8, TicketName.NextCounter(["0001-a", "0007-b", "ABC-12", "notes", "0003-c"]));
    }

    [Theory]
    [InlineData("0012-x", 12)]
    [InlineData("12345-x", 12345)]
    [InlineData("ABC-12", null)]
    [InlineData("0012", null)]
    [InlineData("-x", null)]
    public void The_counter_is_read_from_the_leading_digits(string stem, int? expected)
    {
        Assert.Equal(expected, TicketName.Counter(stem));
    }

    [Fact]
    public void Todos_live_in_the_projects_fleet_folder()
    {
        var root = Path.Combine("r", "proj");

        Assert.Equal(Path.Combine(root, ".fleet", "todos"), TodoPaths.Folder(root));
        Assert.Equal(Path.Combine(root, ".fleet", "todos", "0001-a.md"), TodoPaths.File(root, "0001-a"));
    }
}
