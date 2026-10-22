using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

public sealed class FleetPromptTests
{
    [Fact]
    public void Ctrl_enter_in_the_text_field_submits_the_dialog()
    {
        using var app = Application.Create().Init("ansi");
        var submitted = new List<bool>();
        var (window, _) = Open(app, submitted.Add);

        app.Keyboard.RaiseKeyDownEvent(Key.Enter.WithCtrl);

        Assert.Equal([true], submitted);
        window.Dispose();
    }

    [Fact]
    public void Enter_in_the_text_field_submits_the_prompt_only_once()
    {
        using var app = Application.Create().Init("ansi");
        var submitted = new List<bool>();
        var (window, _) = Open(app, submitted.Add);

        app.Keyboard.RaiseKeyDownEvent(Key.Enter);

        Assert.Equal([false], submitted);
        window.Dispose();
    }

    [Fact]
    public void A_letter_is_typed_as_text_and_does_not_submit()
    {
        using var app = Application.Create().Init("ansi");
        var submitted = new List<bool>();
        var (window, field) = Open(app, submitted.Add);

        app.Keyboard.RaiseKeyDownEvent(Key.A);

        Assert.Empty(submitted);
        Assert.Equal("a", field.Text);
        window.Dispose();
    }

    private static (Window Window, TextField Field) Open(IApplication app, Action<bool> submit)
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
