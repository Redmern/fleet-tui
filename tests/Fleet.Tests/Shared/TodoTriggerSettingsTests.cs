using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Shared;

public class TodoTriggerSettingsTests
{
    [Fact]
    public void The_todo_trigger_defaults_to_plus()
    {
        Assert.Equal("+", SettingsDefaults.TodoTrigger);
        Assert.Equal("+", SettingsConfig.Default.TodoTrigger);
    }

    [Fact]
    public void A_valid_todo_trigger_is_accepted()
    {
        var changed = SettingsConfig.Default.WithTodoTrigger("!");

        Assert.True(changed.Succeeded, changed.Error);
        Assert.Equal("!", changed.Value.TodoTrigger);
    }

    [Fact]
    public void A_todo_trigger_equal_to_the_dispatch_trigger_is_rejected()
    {
        var changed = SettingsConfig.Default.WithTrigger(";").WithTodoTrigger(";");

        Assert.False(changed.Succeeded);
        Assert.Contains("dispatch trigger", changed.Error);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("")]
    [InlineData("++")]
    public void A_todo_trigger_that_is_not_a_single_symbol_is_rejected(string trigger)
    {
        Assert.False(SettingsConfig.Default.WithTodoTrigger(trigger).Succeeded);
    }

    [Fact]
    public void A_todo_trigger_that_clashes_with_the_dispatch_trigger_turns_capture_off_when_merged()
    {
        var merged = (SettingsConfig.Default.WithTrigger("+") with { TodoTrigger = "+" }).MergedOverDefaults();

        Assert.Equal("+", merged.Trigger);
        Assert.Equal(string.Empty, merged.TodoTrigger);
    }

    [Fact]
    public void An_invalid_todo_trigger_merges_back_to_the_default()
    {
        Assert.Equal("+", (SettingsConfig.Default with { TodoTrigger = "x" }).MergedOverDefaults().TodoTrigger);
    }
}
