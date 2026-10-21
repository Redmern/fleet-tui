using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

public sealed class FleetPromptTests
{
    [Fact]
    public void Ctrl_enter_in_the_text_field_submits()
    {
        using var app = Application.Create().Init("ansi");
        var submitted = 0;
        var (window, _) = Open(app, () => submitted++);

        app.Keyboard.RaiseKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal(1, submitted);
        window.Dispose();
    }

    [Fact]
    public void Enter_in_the_text_field_submits_once()
    {
        using var app = Application.Create().Init("ansi");
        var submitted = 0;
        var (window, _) = Open(app, () => submitted++);

        app.Keyboard.RaiseKeyDownEvent(Key.Enter);

        Assert.Equal(1, submitted);
        window.Dispose();
    }

    [Fact]
    public void A_letter_is_typed_as_text_and_does_not_submit()
    {
        using var app = Application.Create().Init("ansi");
        var submitted = 0;
        var (window, field) = Open(app, () => submitted++);

        app.Keyboard.RaiseKeyDownEvent(Key.A);

        Assert.Equal(0, submitted);
        Assert.Equal("a", field.Text);
        window.Dispose();
    }

    private static (Window Window, TextField Field) Open(IApplication app, Action submit)
    {
        var window = new Window { Width = 40, Height = 10 };
        var field = new TextField { Width = 20 };
        FleetPrompt.Submits(field, submit);
        window.Add(field);
        app.Begin(window);
        field.SetFocus();

        return (window, field);
    }
}
