using Fleet.Features.Todos.AddTodo;

namespace Fleet.Tests.Features.Todos;

public class TodoPromptTests
{
    private static bool Known(string name) => name is "techweb" or "fleet";

    [Fact]
    public void A_known_project_prefix_files_the_ticket_there()
    {
        var target = TodoPrompt.Split("techweb: add oauth login", Known);

        Assert.Equal("techweb", target.Project);
        Assert.Equal("add oauth login", target.Text);
    }

    [Theory]
    [InlineData("fix: the flaky test")]
    [InlineData("add oauth login")]
    [InlineData("techweb backend: thing")]
    [InlineData("techweb:")]
    public void Anything_else_stays_with_the_active_project(string prompt)
    {
        var target = TodoPrompt.Split(prompt, Known);

        Assert.Null(target.Project);
        Assert.Equal(prompt, target.Text);
    }
}
