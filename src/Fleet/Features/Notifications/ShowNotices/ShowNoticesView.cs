using Fleet.Ports.Notifications;
using Fleet.Ports.Notifications.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Notifications.ShowNotices;

public static class ShowNoticesView
{
    public const string AllTab = NoticeTabs.AllTab;

    public static void Show(
        IApplication app,
        Keymap keymap,
        INoticeStore store,
        Action<string, IReadOnlyList<string>> dismiss,
        Func<Notice, Task<string?>> open,
        IReadOnlyCollection<string>? only = null,
        NoticeSource? elsewhere = null)
    {
        var projects = only is null
            ? store.Projects()
            : [.. store.Projects().Where(p => only.Contains(p, StringComparer.OrdinalIgnoreCase))];
        var names = NoticeTabs.Names([.. projects, .. elsewhere?.Projects ?? []]);

        var window = FleetTheme.Overlay("notifications", 100, 24);
        var tabBar = FleetTheme.TabBar(1, 0, [.. names.Select(n => $"{n} (0)")]);
        var list = FleetTheme.Rows(1, Pos.Bottom(tabBar.Root), Dim.Fill(2));
        var status = FleetTheme.StatusLine(Pos.AnchorEnd(2));
        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        FleetKeys.ApplyMotions(list, keymap);

        IReadOnlyList<Notice> shown = [];


        void Reload()
        {
            var all = projects.ToDictionary(p => p, store.Load, StringComparer.OrdinalIgnoreCase);
            if (elsewhere is not null)
            {
                var loaded = elsewhere.Load();
                foreach (var project in elsewhere.Projects)
                {
                    all[project] = [.. loaded.Where(n => string.Equals(n.Project, project, StringComparison.OrdinalIgnoreCase))
                        .OrderByDescending(n => n.IsOpen).ThenByDescending(n => n.Since)];
                }
            }

            for (var i = 0; i < names.Count; i++)
            {
                tabBar.Retitle(i, NoticeTabs.Title(names[i], NoticeTabs.For(names[i], all)));
            }

            var tab = names[tabBar.Selected];
            shown = NoticeTabs.For(tab, all);
            FleetRows.Fill(list, NoticeRows.For(shown, withProject: NoticeTabs.ShowsProject(tab), DateTime.UtcNow), FleetRows.Selected(list));
            ShowBar();
        }

        void ShowBar()
        {
            var settings = store.Settings();
            bar.Show(
            [
                ("enter", "open", Open),
                (keymap.DisplayFor(FleetAction.DismissNotice), "dismiss", () => Dismiss(one: true)),
                (keymap.DisplayFor(FleetAction.DismissAllNotices), "dismiss all", () => Dismiss(one: false)),
                ("b", settings.Bell ? "bell on" : "bell off", () => Toggle(bell: true)),
                ("t", settings.Toast ? "toasts on" : "toasts off", () => Toggle(bell: false)),
                ("q/esc", "close", () => app.RequestStop(window)),
            ]);
        }

        void ShowTab(int index)
        {
            tabBar.Select(index);
            FleetRows.Select(list, 0);
            Reload();
        }

        void Dismiss(bool one)
        {
            var chosen = one
                ? shown.Skip(FleetRows.Selected(list)).Take(1).Where(n => n.IsOpen)
                : shown.Where(n => n.IsOpen);

            foreach (var byProject in chosen.GroupBy(n => n.Project, StringComparer.OrdinalIgnoreCase))
            {
                dismiss(byProject.Key, [.. byProject.Select(n => n.Key)]);
            }

            Reload();
        }

        void Toggle(bool bell)
        {
            var settings = store.Settings();
            store.Save(bell ? settings with { Bell = !settings.Bell } : settings with { Toast = !settings.Toast });
            ShowBar();
        }

        void Open()
        {
            var index = FleetRows.Selected(list);
            if (index < 0 || index >= shown.Count)
            {
                return;
            }

            var notice = shown[index];
            _ = Task.Run(async () =>
            {
                var error = await open(notice).ConfigureAwait(false);
                app.Invoke(() =>
                {
                    if (error is null)
                    {
                        app.RequestStop(window);
                    }
                    else
                    {
                        status.Text = error;
                    }
                });
            });
        }

        list.Accepting += (_, e) =>
        {
            Open();
            e.Handled = true;
        };

        tabBar.Chosen += index =>
        {
            if (index != tabBar.Selected)
            {
                ShowTab(index);
            }
        };

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (key == FleetKeys.Cancel || key == keymap.KeyFor(FleetAction.Close))
            {
                app.RequestStop(window);
            }
            else if (key == Key.CursorLeft || key == keymap.KeyFor(FleetAction.PrevTab))
            {
                ShowTab((tabBar.Selected - 1 + names.Count) % names.Count);
            }
            else if (key == Key.CursorRight || key == keymap.KeyFor(FleetAction.NextTab))
            {
                ShowTab((tabBar.Selected + 1) % names.Count);
            }
            else if (key == keymap.KeyFor(FleetAction.DismissNotice))
            {
                Dismiss(one: true);
            }
            else if (key == keymap.KeyFor(FleetAction.DismissAllNotices))
            {
                Dismiss(one: false);
            }
            else if (key == Key.B)
            {
                Toggle(bell: true);
            }
            else if (key == Key.T)
            {
                Toggle(bell: false);
            }
            else
            {
                return;
            }

            key.Handled = true;
        }

        app.Keyboard.KeyDown += Keys;
        var ticking = app.AddTimeout(TimeSpan.FromSeconds(2), () =>
        {
            Reload();
            return true;
        });

        window.Add(tabBar.Root, list, status, bar.Root);
        Reload();

        try
        {
            app.Run(window);
        }
        finally
        {
            if (ticking is not null)
            {
                app.RemoveTimeout(ticking);
            }

            FleetModal.Leave();
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }
    }
}
