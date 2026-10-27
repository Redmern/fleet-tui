using System.Drawing;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Fleet.Tests.Ui;

// Opens each card on the ansi test driver in both corner modes (float-border
// corners, FleetCorners.Rows == 0, and in-card corners with a margin) and
// checks the pill rows sit inside the card and above the action bar.
[Collection(ModalDepthCollection.Name)]
public sealed class FleetCardLayoutTests : IDisposable
{
    public FleetCardLayoutTests() => FloatBorder.Reset();

    public void Dispose() => FloatBorder.Reset();

    public static TheoryData<string, bool> Cards() => new()
    {
        { "prompt", false }, { "prompt", true },
        { "confirm", false }, { "confirm", true },
        { "choose", false }, { "choose", true },
        { "error", false }, { "error", true },
        { "inform", false }, { "inform", true },
    };

    private static void Open(IApplication app, string card)
    {
        switch (card)
        {
            case "prompt":
                FleetPrompt.Text(app, "Rename", label: "New name");
                break;
            case "confirm":
                FleetDialog.Confirm(app, "Remove agent", ["Remove feat/x?"], "Remove");
                break;
            case "choose":
                FleetDialog.Choose(app, "Quit", ["Stop the agents?", "They keep running otherwise."], "Stop", "Leave");
                break;
            case "error":
                FleetDialog.Error(app, "Failed", "It broke.");
                break;
            default:
                FleetDialog.Inform(app, "Done", ["One line.", "Two lines."]);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Cards))]
    public void The_pills_stay_inside_the_card_and_above_the_action_bar(string card, bool floatCorners)
    {
        if (floatCorners)
        {
            Assert.True(FloatBorder.Enable(_ => true));
        }

        Assert.Equal(floatCorners ? 0 : 2 * FleetCorners.Margin, FleetCorners.Rows);

        using var app = Application.Create().Init("ansi");
        Rectangle? pills = null;
        Rectangle? bar = null;
        var inside = 0;

        app.Invoke(() =>
        {
            var window = app.TopRunnableView!;
            window.Layout();
            inside = window.Viewport.Height;
            pills = window.SubViews.OfType<FleetList>()
                .Single(l => l.CanFocus || l.Source!.Count > 1).Frame;
            bar = window.SubViews.Single(v => v.GetType().Name == "ChipStrip") is { Visible: true } strip
                ? strip.Frame
                : null;
            app.RequestStop(app.TopRunnable!);
        });

        Open(app, card);

        Assert.NotNull(pills);
        Assert.True(pills!.Value.Bottom <= inside, $"pills end at row {pills.Value.Bottom}, card has {inside}");

        if (bar is { } shown)
        {
            Assert.True(shown.Bottom <= inside, $"bar ends at row {shown.Bottom}, card has {inside}");
            Assert.True(pills.Value.Bottom <= shown.Y, $"pills end at row {pills.Value.Bottom}, bar starts at {shown.Y}");
        }
        else
        {
            Assert.True(floatCorners, "the action bar is only hidden when the float border shows the buttons");
        }
    }
}
