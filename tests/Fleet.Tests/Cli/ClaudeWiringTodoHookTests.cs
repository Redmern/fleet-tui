using Fleet.Cli.Composition;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Cli;

public class ClaudeWiringTodoHookTests
{
    [Fact]
    public void A_project_with_todo_capture_gets_the_todo_hook_for_that_project()
    {
        Assert.Equal(
            ["hook-todo", "--project", "techweb"],
            ClaudeWiring.TodoHookArgs("techweb", SettingsConfig.Default.MergedOverDefaults()));
    }

    [Fact]
    public void A_project_whose_dispatch_trigger_took_the_todo_sigil_gets_no_todo_hook()
    {
        var settings = SettingsConfig.Default.WithTrigger("+").MergedOverDefaults();

        Assert.Null(ClaudeWiring.TodoHookArgs("techweb", settings));
    }
}
