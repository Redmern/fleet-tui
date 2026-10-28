using Fleet.Ui;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui;

public sealed class FleetTextAreaTests
{
    private static FleetTextArea Area(string text = "")
    {
        var area = new FleetTextArea { Width = 40, Height = 5, Text = text };

        area.MoveEnd();

        return area;
    }

    private static void Type(FleetTextArea area, string text)
    {
        foreach (var c in text)
        {
            area.NewKeyDownEvent(new Key(c));
        }
    }

    [Fact]
    public void Enter_inserts_a_newline_instead_of_submitting()
    {
        var area = Area("fix the build");
        var submitted = false;
        area.Submitting += (_, _) => submitted = true;

        area.NewKeyDownEvent(Key.Enter);
        Type(area, "then test");

        Assert.Equal("fix the build\nthen test", area.Value);
        Assert.False(submitted);
    }

    [Fact]
    public void Ctrl_enter_submits_and_leaves_the_text_alone()
    {
        var area = Area("fix it");
        var submitted = 0;
        area.Submitting += (_, _) => submitted++;

        Assert.True(area.NewKeyDownEvent(Key.Enter.WithCtrl));

        Assert.Equal(1, submitted);
        Assert.Equal("fix it", area.Value);
    }

    [Fact]
    public void Backspace_on_an_empty_area_goes_back()
    {
        var area = Area();
        var back = 0;
        area.GoingBack += (_, _) => back++;

        Assert.True(area.NewKeyDownEvent(Key.Backspace));

        Assert.Equal(1, back);
    }

    [Fact]
    public void Backspace_with_text_deletes_a_character_and_stays()
    {
        var area = Area("ab");
        var back = 0;
        area.GoingBack += (_, _) => back++;

        area.NewKeyDownEvent(Key.Backspace);

        Assert.Equal("a", area.Value);
        Assert.Equal(0, back);
    }

    [Fact]
    public void Backspace_that_empties_the_area_does_not_also_go_back()
    {
        var area = Area("a");
        var back = 0;
        area.GoingBack += (_, _) => back++;

        area.NewKeyDownEvent(Key.Backspace);

        Assert.Equal(string.Empty, area.Value);
        Assert.Equal(0, back);

        area.NewKeyDownEvent(Key.Backspace);

        Assert.Equal(1, back);
    }

    [Fact]
    public void Esc_cancels()
    {
        var area = Area("draft");
        var cancelled = 0;
        area.Cancelling += (_, _) => cancelled++;

        Assert.True(area.NewKeyDownEvent(Key.Esc));

        Assert.Equal(1, cancelled);
    }

    [Fact]
    public void Tab_does_not_insert_a_tab()
    {
        var area = Area("x");

        area.NewKeyDownEvent(Key.Tab);

        Assert.Equal("x", area.Value);
    }

    [Theory]
    [InlineData("a\r\nb", "a\nb")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\rb", "a\nb")]
    public void The_value_always_uses_plain_newlines(string text, string value) =>
        Assert.Equal(value, FleetTextArea.Normalize(text));
}
