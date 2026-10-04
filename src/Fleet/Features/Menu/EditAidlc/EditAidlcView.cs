using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Menu.EditAidlc;

public static class EditAidlcView
{
    public static void Show(
        IApplication app,
        Keymap keymap,
        string project,
        SettingsConfig config,
        Func<SettingsConfig, string?> save)
    {
        var window = FleetTheme.Overlay(AidlcRows.Title(project));

        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));
        var status = FleetTheme.Caption(1, Pos.AnchorEnd(2), string.Empty);

        FleetRows.Fill(list, AidlcRows.For(config.Aidlc));
        FleetKeys.ApplyMotions(list, keymap);

        void Persist(AidlcSettings next, string message, int index)
        {
            config = config.WithAidlc(next);

            var trouble = save(config);

            status.Text = trouble is { Length: > 0 } ? trouble : message;
            FleetRows.Fill(list, AidlcRows.For(config.Aidlc), index);
        }

        int? Pick(string label, IReadOnlyList<PickerEntry> entries, int current) =>
            FleetPicker.Choose(app, $"{label} — {project}", entries, keymap, current);

        void Change()
        {
            var index = FleetRows.Selected(list);
            var aidlc = config.Aidlc;

            if (AidlcRows.IsModeRow(index))
            {
                if (Pick(AidlcRows.ModeLabel, AidlcRows.ModeEntries(), (int)aidlc.Mode) is { } mode)
                {
                    Persist(aidlc with { Mode = (AidlcMode)mode }, "Saved.", index);
                }

                return;
            }

            if (AidlcRows.IsProfileRow(index))
            {
                if (Pick(AidlcRows.ProfileLabel, AidlcRows.ProfileEntries(), (int)aidlc.DefaultProfile) is { } profile)
                {
                    Persist(aidlc with { DefaultProfile = (Profile)profile }, "Saved.", index);
                }

                return;
            }

            if (AidlcRows.IsAutonomyRow(index))
            {
                if (Pick(AidlcRows.AutonomyLabel, AidlcRows.AutonomyEntries(), (int)aidlc.Autonomy) is { } autonomy)
                {
                    Persist(aidlc with { Autonomy = (Autonomy)autonomy }, "Saved.", index);
                }

                return;
            }

            var part = AidlcRows.PartAt(index);

            if (part != AidlcPart.None)
            {
                var on = !aidlc.IsOn(part);

                Persist(
                    aidlc.With(part, on),
                    on ? $"{AidlcRows.Describe(part)} is on." : $"{AidlcRows.Describe(part)} is {AidlcRows.WhenOff(part)}.",
                    index);
            }
        }

        void Reset()
        {
            var index = FleetRows.Selected(list);
            var aidlc = config.Aidlc;
            var shipped = SettingsDefaults.Aidlc;

            var next = index switch
            {
                _ when AidlcRows.IsModeRow(index) => aidlc with { Mode = shipped.Mode },
                _ when AidlcRows.IsProfileRow(index) => aidlc with { DefaultProfile = shipped.DefaultProfile },
                _ when AidlcRows.IsAutonomyRow(index) => aidlc with { Autonomy = shipped.Autonomy },
                _ when AidlcRows.PartAt(index) is var part && part != AidlcPart.None => aidlc.With(part, shipped.IsOn(part)),
                _ => aidlc,
            };

            Persist(next, "Reset to the default.", index);
        }

        list.Accepting += (_, e) =>
        {
            Change();
            e.Handled = true;
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", "change", Change),
            ("r", "default", Reset),
            ("esc", "close", () => app.RequestStop(window)),
            ("bksp", "back", () =>
            {
                FleetModal.Back();
                app.RequestStop(window);
            }),
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

            if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            if (key == Key.R)
            {
                Reset();
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        window.Add(list, status, bar.Root);

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
