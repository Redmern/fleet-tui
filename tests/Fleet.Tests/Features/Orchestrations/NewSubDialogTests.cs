using Fleet.Features.Orchestrations.Dispatch;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Orchestrations;

// Drives the real dialog on the ansi test driver: the keys are queued onto
// the run loop, so they arrive the way a terminal would deliver them.
[Collection(ModalDepthCollection.Name)]
public sealed class NewSubDialogTests
{
    private static readonly IReadOnlyList<string> Past = ["fix the build", "write the release notes"];

    private static (string? Task, bool WentBack) Run(IReadOnlyList<string> past, params Key[] keys)
    {
        using var app = Application.Create().Init("ansi");

        app.Invoke(() =>
        {
            foreach (var key in keys)
            {
                app.Keyboard.RaiseKeyDownEvent(key);
            }

            app.Keyboard.RaiseKeyDownEvent(Key.Esc);
        });

        var task = NewSubView.Show(app, past, Keymap.Default);

        return (task, FleetModal.WentBack());
    }

    private static Key[] Typed(string text) => [.. text.Select(c => new Key(c))];

    [Fact]
    public void Enter_adds_a_line_and_ctrl_enter_dispatches_the_whole_task()
    {
        var (task, _) = Run([], [.. Typed("fix"), Key.Enter, .. Typed("it"), Key.Enter.WithCtrl]);

        Assert.Equal("fix\nit", task);
    }

    [Fact]
    public void Ctrl_enter_on_an_empty_task_does_not_dispatch_and_keeps_the_dialog_open()
    {
        var (task, wentBack) = Run([], Key.Enter.WithCtrl);

        Assert.Null(task);
        Assert.False(wentBack);
    }

    [Fact]
    public void Ctrl_enter_on_a_blank_task_keeps_the_dialog_open_for_a_real_one()
    {
        var (task, _) = Run([], [.. Typed("  "), Key.Enter, Key.Enter.WithCtrl, Key.Backspace, Key.Backspace, Key.Backspace, .. Typed("go"), Key.Enter.WithCtrl]);

        Assert.Equal("go", task);
    }

    [Fact]
    public void Backspace_on_an_empty_task_goes_back()
    {
        var (task, wentBack) = Run([], Key.Backspace);

        Assert.Null(task);
        Assert.True(wentBack);
    }

    [Fact]
    public void Backspace_while_the_task_has_text_only_deletes()
    {
        var (task, wentBack) = Run([], [.. Typed("ab"), Key.Backspace, Key.Enter.WithCtrl]);

        Assert.Equal("a", task);
        Assert.False(wentBack);
    }

    [Fact]
    public void Esc_cancels()
    {
        var (task, wentBack) = Run(Past, Typed("draft"));

        Assert.Null(task);
        Assert.False(wentBack);
    }

    [Fact]
    public void Choosing_a_past_task_fills_the_field()
    {
        var (task, _) = Run(Past, Key.Tab, Key.Enter, Key.Enter.WithCtrl);

        Assert.Equal("fix the build", task);
    }

    [Fact]
    public void J_moves_down_the_past_tasks()
    {
        var (task, _) = Run(Past, Key.Tab, new Key('j'), Key.Enter, Key.Enter.WithCtrl);

        Assert.Equal("write the release notes", task);
    }

    [Fact]
    public void The_p_jump_key_focuses_the_past_tasks_and_t_returns_to_the_task()
    {
        var (task, _) = Run(Past, [Key.Tab, new Key('t'), .. Typed("new"), Key.Enter.WithCtrl]);

        Assert.Equal("new", task);
    }

    [Fact]
    public void Typing_filters_the_past_tasks()
    {
        var (task, _) = Run(Past, [.. Typed("release"), Key.Tab, Key.Enter, Key.Enter.WithCtrl]);

        Assert.Equal("write the release notes", task);
    }

    [Fact]
    public void The_d_key_in_the_list_dispatches()
    {
        var (task, _) = Run(Past, [.. Typed("go"), Key.Tab, Key.D]);

        Assert.Equal("go", task);
    }

    [Fact]
    public void Backspace_in_the_past_list_goes_back()
    {
        var (task, wentBack) = Run(Past, [.. Typed("go"), Key.Tab, Key.Backspace]);

        Assert.Null(task);
        Assert.True(wentBack);
    }
}
