using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Fleet.Tests.Ui.Harnesses;

// Opens one real Fleet view on the ansi test driver and drives it with keys.
// Each key is its own queued invoke; a view still open after the keys is stopped
// and reported open, so a view that swallows a key cannot hang the run.
// FleetModal keeps the open-dialog depth in static state, so every test that uses
// this harness joins the modal-depth collection.
public static class HeadlessUi
{
    public static HeadlessRun<T> Run<T>(Func<IApplication, T> show, params Key[] keys)
    {
        using var app = Application.Create().Init("ansi");
        var leftOpen = false;

        foreach (var key in keys)
        {
            app.Invoke(() => app.Keyboard.RaiseKeyDownEvent(key));
        }

        app.Invoke(() =>
        {
            if (app.TopRunnable is { StopRequested: false } still)
            {
                leftOpen = true;
                app.RequestStop(still);
            }
        });

        var result = show(app);

        return new HeadlessRun<T>(result, leftOpen, FleetModal.WentBack());
    }
}

public sealed record HeadlessRun<T>(T Result, bool LeftOpen, bool WentBack);
