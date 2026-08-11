using Fleet.Features.Repositories.Secrets.Models;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Repositories.Secrets;

public static class SecretsView
{
    public static string? Show(
        IApplication app,
        Keymap keymap,
        SecretsPlan plan,
        Func<SecretsPlan, string?> open,
        Func<SecretsPlan, string?> copy)
    {
        string? status = null;

        var window = FleetTheme.Overlay(SecretsRows.Title(plan));

        var list = FleetTheme.Rows(1, 2, Dim.Fill(3));

        FleetRows.Fill(list, SecretsRows.For(plan));
        FleetKeys.ApplyMotions(list, keymap);

        void Act(string action)
        {
            status = action == SecretsRows.Open ? open(plan) : copy(plan);

            app.RequestStop(window);
        }

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("o", SecretsRows.Open, () => Act(SecretsRows.Open)),
            ("c", SecretsRows.Copy, () => Act(SecretsRows.Copy)),
            ("esc", "back", () => app.RequestStop(window)),
        ]);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            if (key == Key.O)
            {
                Act(SecretsRows.Open);
                key.Handled = true;
                return;
            }

            if (key == Key.C)
            {
                Act(SecretsRows.Copy);
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        window.Add(FleetTheme.Caption(1, 0, plan.Root), list, bar.Root);

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

        return status;
    }
}
