using Fleet.Features.Menu.ShowReleaseNotes.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Menu.ShowReleaseNotes;

public static class ReleaseNotesView
{
    public const string Title = "what's new";

    public static void Show(IApplication app, Keymap keymap, IReadOnlyList<ReleaseGroup> groups)
    {
        var window = FleetTheme.Overlay(Title);

        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));

        FleetRows.Fill(list, ReleaseNotesRows.For(groups));
        FleetKeys.ApplyMotions(list, keymap);

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show([("esc", FleetIcons.Back, () => app.RequestStop(window))]);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (FleetKeys.GoesBack(key))
            {
                FleetModal.Back();
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            if (key == FleetKeys.Cancel || key == keymap.KeyFor(Shared.Keymap.Enums.FleetAction.Close))
            {
                app.RequestStop(window);
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        window.Add(list, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }
    }
}
