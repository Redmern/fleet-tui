using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Menu.EditSettings;

public static class EditSettingsView
{
    public static void Show(
        IApplication app,
        Keymap keymap,
        string project,
        SettingsConfig config,
        Func<SettingsConfig, string?> save)
    {
        var window = FleetTheme.Overlay(SettingsRows.Title(project));

        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));
        var status = FleetTheme.Caption(1, Pos.AnchorEnd(2), string.Empty);

        FleetRows.Fill(list, SettingsRows.For(config));
        FleetKeys.ApplyMotions(list, keymap);

        void Refill(int index)
        {
            FleetRows.Fill(list, SettingsRows.For(config), index);
        }

        void Persist(SettingsConfig next, string message)
        {
            config = next;

            var trouble = save(config);

            status.Text = trouble is { Length: > 0 } ? trouble : message;
        }

        void EditTrigger()
        {
            var captured = FleetKeyCapture.Show(app, SettingsRows.TriggerLabel);

            if (captured is null)
            {
                status.Text = "Unchanged.";
                return;
            }

            var trigger = DispatchTrigger.FromKeyText(captured);

            if (trigger.Length == 0)
            {
                status.Text = "A letter or digit cannot be the dispatch trigger.";
                return;
            }

            Persist(config.WithTrigger(trigger), $"The dispatch trigger is now '{trigger}'.");
            Refill(0);
        }

        void EditChannel(HarnessTool tool, int index)
        {
            if (config.RuleFor(tool).Policy != ActionPolicy.Ask)
            {
                status.Text = "The channel only matters when the policy is ask.";
                return;
            }

            var picked = FleetPicker.Choose(
                app,
                $"{SettingsDefaults.Describe(tool)} — channel",
                SettingsRows.ChannelEntries(),
                keymap,
                (int)config.RuleFor(tool).Channel);

            if (picked is null)
            {
                return;
            }

            Persist(config.With(tool, (AskChannel)picked.Value), "Saved.");
            Refill(index);
        }

        void EditPolicy(HarnessTool tool, int index)
        {
            var picked = FleetPicker.Choose(
                app,
                $"{SettingsDefaults.Describe(tool)} — policy",
                SettingsRows.PolicyEntries(),
                keymap,
                (int)config.RuleFor(tool).Policy);

            if (picked is null)
            {
                return;
            }

            Persist(config.With(tool, (ActionPolicy)picked.Value), "Saved.");
            Refill(index);

            if ((ActionPolicy)picked.Value == ActionPolicy.Ask)
            {
                EditChannel(tool, index);
            }
        }

        void Change()
        {
            var index = FleetRows.Selected(list);

            if (SettingsRows.IsTriggerRow(index))
            {
                EditTrigger();
                return;
            }

            var tool = SettingsRows.ToolAt(index);

            if (tool != HarnessTool.None)
            {
                EditPolicy(tool, index);
            }
        }

        void Channel()
        {
            var index = FleetRows.Selected(list);
            var tool = SettingsRows.ToolAt(index);

            if (tool != HarnessTool.None)
            {
                EditChannel(tool, index);
            }
        }

        void Reset()
        {
            var index = FleetRows.Selected(list);

            if (SettingsRows.IsTriggerRow(index))
            {
                Persist(config.WithTrigger(SettingsDefaults.Trigger), "Reset to the default.");
                Refill(index);
                return;
            }

            var tool = SettingsRows.ToolAt(index);

            if (tool != HarnessTool.None)
            {
                var rule = SettingsDefaults.RuleFor(tool);

                Persist(config.With(tool, rule.Policy).With(tool, rule.Channel), "Reset to the default.");
                Refill(index);
            }
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
            ("c", "channel", Channel),
            ("r", "default", Reset),
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

            if (key == Key.C)
            {
                Channel();
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
