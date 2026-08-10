using System.Collections.ObjectModel;
using Fleet.Features.Agents.ListAgents.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Agents.ListAgents;

public static class ListAgentsView
{
    public const string NoneOpen = "(no agents running)";

    public const string NoneHidden = "(no hidden agents)";

    public static void Show(
        IApplication app, AgentListing listing, Keymap keymap, Func<int, int, string?> open)
    {
        var window = FleetTheme.Overlay("Agents");

        var tabBar = FleetTheme.TabBar(1, 0,
            [$"Open ({listing.Open.Count})", $"Hidden ({listing.Hidden.Count})"]);

        var openList = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));
        var hiddenList = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));

        openList.SetSource(new ObservableCollection<string>(
            listing.Open.Count == 0 ? [NoneOpen] : AgentRows.For(listing.Open).ToList()));

        hiddenList.SetSource(new ObservableCollection<string>(
            listing.Hidden.Count == 0 ? [NoneHidden] : AgentRows.For(listing.Hidden).ToList()));

        var lists = new[] { openList, hiddenList };
        var status = FleetTheme.Caption(1, Pos.AnchorEnd(2), string.Empty);

        FleetKeys.ApplyMotions(openList, keymap);
        FleetKeys.ApplyMotions(hiddenList, keymap);

        void ShowTab(int index)
        {
            tabBar.Select(index);

            for (var i = 0; i < lists.Length; i++)
            {
                lists[i].Visible = i == index;
            }

            lists[index].SetFocus();
            window.SetNeedsDraw();
        }

        void Open()
        {
            var tab = tabBar.Selected;

            if (listing.For(tab).Count == 0)
            {
                return;
            }

            var error = open(tab, lists[tab].SelectedItem ?? 0);

            if (error is not null)
            {
                status.Text = error;
                return;
            }

            app.RequestStop(window);
        }

        foreach (var list in lists)
        {
            list.Accepting += (_, e) =>
            {
                Open();
                e.Handled = true;
            };
        }

        void Keys(object? sender, Key key)
        {
            if (key == FleetKeys.Cancel || key == keymap.KeyFor(Shared.Keymap.Enums.FleetAction.Close))
            {
                app.RequestStop(window);
                key.Handled = true;
                return;
            }

            if (key == Key.CursorLeft || key == keymap.KeyFor(Shared.Keymap.Enums.FleetAction.PrevTab))
            {
                ShowTab(AgentListing.OpenTab);
                key.Handled = true;
                return;
            }

            if (key == Key.CursorRight || key == keymap.KeyFor(Shared.Keymap.Enums.FleetAction.NextTab))
            {
                ShowTab(AgentListing.HiddenTab);
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        window.Add(
            tabBar.Root,
            openList,
            hiddenList,
            status,
            FleetTheme.HintBar(FleetHints.AgentList));

        ShowTab(AgentListing.OpenTab);

        try
        {
            app.Run(window);
        }
        finally
        {
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }
    }
}
