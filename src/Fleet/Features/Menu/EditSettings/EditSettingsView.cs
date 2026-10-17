using Fleet.Shared.Keymap.Enums;
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

        var tabBar = FleetTheme.TabBar(1, 0, SettingsTabs.Titles);
        var list = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));
        var status = FleetTheme.Caption(1, Pos.AnchorEnd(2), string.Empty);
        var tab = SettingsTabs.All;
        IReadOnlyList<int> visible = [];

        FleetKeys.ApplyMotions(list, keymap);

        int Current()
        {
            var position = FleetRows.Selected(list);

            return position >= 0 && position < visible.Count ? visible[position] : -1;
        }

        void Explain()
        {
            var index = Current();

            status.Text = index >= 0
                ? SettingsRows.Explain(config, index)
                : $"No tool asks through the {SettingsTabs.Titles[tab].ToLowerInvariant()}.";
        }

        void Refill(int index)
        {
            var position = visible.ToList().IndexOf(index);

            visible = SettingsTabs.Visible(tab, config);

            var all = SettingsRows.For(config);
            var landed = visible.ToList().IndexOf(index);

            FleetRows.Fill(list, [.. visible.Select(i => all[i])], landed >= 0 ? landed : Math.Max(0, position));
        }

        void Persist(SettingsConfig next, string message, int index)
        {
            config = next;

            var trouble = save(config);

            Refill(index);
            status.Text = trouble is { Length: > 0 } ? trouble : message;
        }

        void SwitchTo(int next)
        {
            var index = Current();

            tab = next;
            tabBar.Select(tab);
            Refill(index);
            Explain();
        }

        tabBar.Chosen += SwitchTo;
        list.ValueChanged += (_, _) => Explain();
        Refill(0);

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

            if (config.DispatchTriggerRefusal(trigger) is { } refusal)
            {
                status.Text = refusal;
                return;
            }

            Persist(config.WithTrigger(trigger), $"The dispatch trigger is now '{trigger}'.", 0);
        }

        void Change()
        {
            var index = Current();

            if (index < 0)
            {
                return;
            }

            if (SettingsRows.IsTriggerRow(index))
            {
                EditTrigger();
                return;
            }

            var next = SettingsRows.Cycled(config, index);

            if (next == config)
            {
                return;
            }

            var message = SettingsRows.IsStatusHooksRow(index)
                ? next.StatusHooks
                    ? "Status hooks on: agents report their status through Claude hooks."
                    : "Status hooks off: fleet reads the panes instead."
                : "Saved.";

            Persist(next, message, index);
        }

        void SetPolicy(ActionPolicy policy)
        {
            var index = Current();
            var next = SettingsRows.WithPolicy(config, index, policy);

            if (next == config)
            {
                return;
            }

            Persist(next, "Saved.", index);
        }

        void Channel()
        {
            var index = Current();

            if (index < 0)
            {
                return;
            }

            if (SettingsRows.ChannelRefusal(config, index) is { } refusal)
            {
                status.Text = refusal;
                return;
            }

            var next = SettingsRows.WithNextChannel(config, index);

            Persist(next, SettingsRows.Explain(next, index), index);
        }

        void Reset()
        {
            var index = Current();

            if (index < 0)
            {
                return;
            }

            if (SettingsRows.IsTriggerRow(index))
            {
                Persist(config.WithTrigger(SettingsDefaults.Trigger), "Reset to the default.", index);
                return;
            }

            if (SettingsRows.IsCommitRow(index))
            {
                Persist(config.WithCommit(SettingsDefaults.Commit), "Reset to the default.", index);
                return;
            }

            if (SettingsRows.IsPushRow(index))
            {
                Persist(config.WithPush(SettingsDefaults.Push), "Reset to the default.", index);
                return;
            }

            if (SettingsRows.IsMergeRow(index))
            {
                Persist(config.WithMerge(SettingsDefaults.Merge), "Reset to the default.", index);
                return;
            }

            if (SettingsRows.IsStatusHooksRow(index))
            {
                Persist(config.WithStatusHooks(SettingsDefaults.StatusHooks), "Reset to the default.", index);
                return;
            }

            var tool = SettingsRows.ToolAt(index);

            if (tool != HarnessTool.None)
            {
                var rule = SettingsDefaults.RuleFor(tool);

                Persist(config.With(tool, rule.Policy).With(tool, rule.Channel), "Reset to the default.", index);
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
            ("a", "allow", () => SetPolicy(ActionPolicy.Allow)),
            ("s", "ask", () => SetPolicy(ActionPolicy.Ask)),
            ("f", "forbid", () => SetPolicy(ActionPolicy.Forbid)),
            ("c", SettingsRows.ChannelChip, Channel),
            ("r", "default", Reset),
            ("bksp", FleetIcons.Back, () =>
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

            if (SettingsRows.PolicyForKey(key) is { } policy)
            {
                SetPolicy(policy);
                key.Handled = true;
                return;
            }

            if (key == Key.Tab || key == Key.CursorRight || key == keymap.KeyFor(FleetAction.NextTab))
            {
                SwitchTo(SettingsTabs.Next(tab));
                key.Handled = true;
                return;
            }

            if (key == Key.Tab.WithShift || key == Key.CursorLeft || key == keymap.KeyFor(FleetAction.PrevTab))
            {
                SwitchTo(SettingsTabs.Previous(tab));
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

        window.Add(tabBar.Root, list, status, bar.Root);
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
