using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class CursorHeartbeat
{
    public static void Quiet(IApplication app) =>
        app.Iteration += (_, _) =>
        {
            if (app.Driver is { } driver)
            {
                Settle(driver, app.TopRunnableView?.MostFocused);
            }
        };

    private static void Settle(IDriver driver, View? focused)
    {
        if (driver.GetCursorNeedsUpdate() && Settled(driver.GetCursor(), focused))
        {
            driver.SetCursorNeedsUpdate(false);
        }
    }

    public static bool Settled(Cursor written, View? focused) =>
        written == (focused is { Cursor.IsVisible: true } ? focused.Cursor : new Cursor());
}
