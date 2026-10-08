using Fleet.Features.Updates.ShowVersion.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Updates.ShowVersion;

public static class ShowVersionView
{
    public const string Latest = "latest";

    public static string? Show(IApplication app, Keymap keymap, VersionScreen screen)
    {
        string? chosen = null;

        var window = FleetTheme.Overlay("fleet version");
        var summary = VersionRows.Summary(screen);

        for (var i = 0; i < summary.Count; i++)
        {
            window.Add(FleetTheme.Caption(2, 1 + i, summary[i]));
        }

        var header = FleetTheme.SectionHeader(1, summary.Count + 2, "Releases");
        var list = FleetTheme.Rows(1, Pos.Bottom(header), Dim.Fill(3));
        var status = FleetTheme.StatusLine(Pos.AnchorEnd(2));

        FleetRows.Fill(list, VersionRows.Rows(screen));
        FleetKeys.ApplyMotions(list, keymap);

        if (screen.Releases.Count == 0)
        {
            status.Text = "no releases listed: check your connection, or FLEET_REPO";
        }

        void Install()
        {
            var index = FleetRows.Selected(list);

            if (index < 0 || index >= screen.Releases.Count)
            {
                return;
            }

            chosen = screen.Releases[index].Tag;
            app.RequestStop(window);
        }

        void Update()
        {
            if (!screen.UpdateAvailable)
            {
                return;
            }

            chosen = Latest;
            app.RequestStop(window);
        }

        list.Accepting += (_, e) =>
        {
            Install();
            e.Handled = true;
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", "install", Install),
            .. screen.UpdateAvailable
                ? [(keymap.DisplayFor(FleetAction.UpdateFleet), "update", Update)]
                : Array.Empty<(string, string, Action)>(),
        ]);

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

            if (key == FleetKeys.Cancel || key == keymap.KeyFor(FleetAction.Close))
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            if (key == keymap.KeyFor(FleetAction.UpdateFleet))
            {
                Update();
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        window.Add(header, list, status, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window), header);

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

        return chosen;
    }
}
