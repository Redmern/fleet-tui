using Fleet.Ports.Keymap;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Menu.EditKeybinds;

public static class EditKeybindsView
{
    private const string PrefixRow = "prefix";

    public static KeymapConfig Show(IApplication app, IKeymapStore store, Keymap keymap)
    {
        var config = keymap.Config;
        var tabs = EditKeybindsGrid.Tabs(EditKeybindsGrid.Boxes(EditKeybindsRows.Build()));
        var current = 0;
        var spot = EditKeybindsGrid.First();

        var window = FleetTheme.Overlay("Keybinds");
        var tabBar = FleetTheme.TabBar(1, 0, [.. tabs.Select(t => t.Title)]);
        var canvas = new FleetCanvas(1, Pos.Bottom(tabBar.Root), Dim.Fill(3));
        var status = FleetTheme.Caption(1, Pos.AnchorEnd(3), string.Empty);

        IReadOnlyList<EditKeybindsBox> Boxes() => tabs.Count == 0 ? [] : tabs[current].Boxes;

        string KeyOf(EditKeybindsRow row) =>
            FleetKeyText.Display(row.Action is null ? config.Prefix : Binding(config, row.Action.Value));

        void Fill()
        {
            var boxes = Boxes();

            if (boxes.Count == 0)
            {
                return;
            }

            spot = EditKeybindsGrid.Clamp(boxes, spot);
            tabBar.Select(current);

            var layout = EditKeybindsGrid.Layout(
                boxes, EditKeybindsGrid.KeyWidth(boxes, KeyOf), canvas.Viewport.Width);

            var picture = EditKeybindsGrid.Draw(boxes, layout, KeyOf, spot);
            canvas.Show(picture.Lines, picture.Highlight);
        }

        void Move(EditKeybindsSpot next)
        {
            spot = next;
            Fill();
        }

        void SwitchTo(int tab)
        {
            if (tabs.Count == 0 || tab == current)
            {
                return;
            }

            current = tab;
            spot = EditKeybindsGrid.First();
            Fill();
        }

        void Rebind()
        {
            var boxes = Boxes();

            if (boxes.Count == 0)
            {
                return;
            }

            var row = boxes[spot.Box].Cells[spot.Cell];
            var target = row.Action is null ? PrefixRow : row.Action.Value.ToString();
            var captured = FleetKeyCapture.Show(app, target);

            if (captured is null)
            {
                status.Text = "Unchanged.";
                return;
            }

            var candidate = row.Action is null
                ? config.WithPrefix(captured)
                : config.With(row.Action.Value, captured);

            if (row.Action is { } action && new Keymap(candidate).ClashFor(action) is var clash and not FleetAction.None)
            {
                status.Text = $"Unchanged. {FleetKeyText.Display(captured)} is already {KeymapDefaults.Describe(clash)}.";
                return;
            }

            config = candidate;
            store.Save(config);
            Fill();
            status.Text = $"Saved. {target} is now {FleetKeyText.Display(captured)}. Reopen panes to apply.";
        }

        canvas.FrameChanged += (_, _) => Fill();
        tabBar.Chosen += SwitchTo;

        canvas.KeyDown += (_, key) =>
        {
            if (key == Key.Esc)
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            if (FleetKeys.GoesBack(key))
            {
                FleetModal.Back();
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            var boxes = Boxes();

            if (boxes.Count == 0)
            {
                return;
            }

            if (key == Key.Enter)
            {
                Rebind();
            }
            else if (key == Key.CursorLeft || key == keymap.KeyFor(FleetAction.PrevTab))
            {
                SwitchTo((current - 1 + tabs.Count) % tabs.Count);
            }
            else if (key == Key.CursorRight || key == keymap.KeyFor(FleetAction.NextTab))
            {
                SwitchTo((current + 1) % tabs.Count);
            }
            else if (key == Key.CursorDown || key == keymap.KeyFor(FleetAction.MoveDown))
            {
                Move(EditKeybindsGrid.Down(boxes, spot));
            }
            else if (key == Key.CursorUp || key == keymap.KeyFor(FleetAction.MoveUp))
            {
                Move(EditKeybindsGrid.Up(boxes, spot));
            }
            else if (key == Key.Home || key == keymap.KeyFor(FleetAction.MoveFirst))
            {
                Move(EditKeybindsGrid.First());
            }
            else if (key == Key.End || key == keymap.KeyFor(FleetAction.MoveLast))
            {
                Move(EditKeybindsGrid.Last(boxes));
            }
            else
            {
                return;
            }

            key.Handled = true;
        };

        window.Add(tabBar.Root, canvas, status, FleetTheme.HintBar(FleetHints.Keybinds));

        FleetModal.Enter();

        try
        {
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            window.Dispose();
        }

        return config;
    }

    private static string Binding(KeymapConfig config, FleetAction action) =>
        config.Bindings.TryGetValue(action, out var key) ? key : string.Empty;
}
