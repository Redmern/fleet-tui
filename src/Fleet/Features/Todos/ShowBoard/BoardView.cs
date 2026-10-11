using Fleet.Features.Todos.ShowBoard.Enums;
using Fleet.Features.Todos.ShowBoard.Models;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Todos.ShowBoard;

public static class BoardView
{
    public static readonly TimeSpan Refresh = TimeSpan.FromSeconds(2);

    public static void Show(
        IApplication app,
        Keymap keymap,
        BoardScope scope,
        BoardTickets board,
        Func<BoardProject, string, Task<Result>> open,
        Func<BoardProject, string, Task<Result<Ticket>>> launch,
        IReadOnlyList<BoardJira> jira)
    {
        var window = FleetTheme.Overlay(scope.Title);

        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));
        var status = FleetTheme.Caption(1, Pos.AnchorEnd(2), string.Empty);

        FleetKeys.ApplyMotions(list, keymap);

        IReadOnlyList<BoardEntry> tickets = [];
        var shown = string.Empty;
        var busy = false;
        var syncing = false;
        var closed = false;
        using var stop = new CancellationTokenSource();

        BoardEntry? Selected() =>
            tickets.Count == 0 ? null : tickets[Math.Clamp(FleetRows.Selected(list), 0, tickets.Count - 1)];

        IReadOnlyList<FleetRow> Rows(IReadOnlyList<BoardEntry> entries) => BoardRows.For(entries, scope.ShowsProject);

        void Fill(BoardEntry? keep = null, bool follow = true)
        {
            var at = FleetRows.Selected(list);
            keep = follow ? keep ?? Selected() : null;

            tickets = board.List(scope.Projects);

            var rows = Rows(tickets);
            shown = string.Join('\n', rows.Select(r => r.Text));

            FleetRows.Fill(list, rows, BoardRows.IndexOf(tickets, keep, at));

            if (tickets.Count == 0)
            {
                status.Text = BoardRows.Empty;
            }
            else if (status.Text == BoardRows.Empty)
            {
                status.Text = string.Empty;
            }
        }

        bool Changed() =>
            string.Join('\n', Rows(board.List(scope.Projects)).Select(r => r.Text)) != shown;

        void Apply(BoardProject project, Result<Ticket> result, Func<Ticket, string> message, bool follow)
        {
            status.Text = result.Succeeded ? message(result.Value) : result.Error!;
            Fill(result.Succeeded && follow ? new BoardEntry(project, result.Value) : null, follow: result.Succeeded && follow);
        }

        async Task OpenAsync(BoardEntry entry)
        {
            var opened = await BoardRows.SafelyAsync(() => open(entry.Project, entry.Ticket.Id)).ConfigureAwait(false);

            await FleetAsync.OnUi(app, () =>
            {
                busy = false;

                if (opened.Succeeded)
                {
                    app.RequestStop(window);
                }
                else
                {
                    status.Text = opened.Error!;
                }

                return true;
            }).ConfigureAwait(false);
        }

        async Task LaunchAsync(BoardEntry entry)
        {
            var launched = await BoardRows.SafelyAsync(() => launch(entry.Project, entry.Ticket.Id)).ConfigureAwait(false);

            await FleetAsync.OnUi(app, () =>
            {
                busy = false;
                Apply(entry.Project, launched, BoardRows.Launched, follow: true);

                return true;
            }).ConfigureAwait(false);
        }

        async Task SyncAsync()
        {
            var pulled = await Task.Run(() => BoardPull.AllAsync(jira, stop.Token)).ConfigureAwait(false);

            await FleetAsync.OnUi(app, () =>
            {
                syncing = false;

                if (!closed)
                {
                    Fill();
                    status.Text = BoardRows.JiraSynced(pulled, scope.ShowsProject);
                }

                return true;
            }).ConfigureAwait(false);
        }

        void Sync()
        {
            if (jira.Count == 0 || syncing)
            {
                return;
            }

            syncing = true;
            status.Text = BoardRows.JiraSyncing;
            _ = SyncAsync();
        }

        void Quit() => app.RequestStop(window);

        void Run(BoardCommand command)
        {
            if (command == BoardCommand.Quit)
            {
                Quit();
                return;
            }

            if (command == BoardCommand.Refresh)
            {
                Fill();
                status.Text = tickets.Count == 0 ? BoardRows.Empty : "Refreshed.";
                Sync();
                return;
            }

            if (Selected() is not { } ticket || busy)
            {
                return;
            }

            switch (command)
            {
                case BoardCommand.Open:
                    busy = true;
                    status.Text = $"Opening {ticket.Ticket.Id}...";
                    _ = OpenAsync(ticket);
                    break;

                case BoardCommand.Launch:
                    busy = true;
                    status.Text = $"Launching {ticket.Ticket.Id}...";
                    _ = LaunchAsync(ticket);
                    break;

                case BoardCommand.Effort:
                    Apply(ticket.Project, board.CycleEffort(ticket.Project.Root, ticket.Ticket.Id), BoardRows.EffortSaved, follow: true);
                    break;

                case BoardCommand.Close:
                    Apply(ticket.Project, board.Close(ticket.Project.Root, ticket.Ticket.Id), t => $"Closed {t.Id}. The file stays in .fleet/todos.", follow: false);
                    break;
            }
        }

        list.Accepting += (_, e) =>
        {
            Run(BoardCommand.Open);
            e.Handled = true;
        };

        list.MouseEvent += (_, mouse) =>
        {
            if (mouse.Position is not { } at || !(mouse.IsSingleClicked || mouse.IsDoubleClicked))
            {
                return;
            }

            var row = list.Viewport.Y + at.Y;
            if (list.Source is not FleetRowSource source || !source.Holds(row))
            {
                return;
            }

            var item = source.ItemAt(row);

            var again = item == FleetRows.Selected(list);

            FleetRows.Select(list, item);
            mouse.Handled = true;

            if (again || mouse.IsDoubleClicked)
            {
                Run(BoardCommand.Open);
            }
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            .. BoardKeys.Hints(keymap).Select(h => (h.Key, h.Label, (Action)(() => Run(h.Command)))),
            ("bksp", FleetIcons.Back, () =>
            {
                FleetModal.Back();
                Quit();
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
                Quit();
                key.Handled = true;
                return;
            }

            if (key == FleetKeys.Cancel)
            {
                Quit();
                key.Handled = true;
                return;
            }

            if (BoardKeys.For(key, keymap) is var command and not BoardCommand.None)
            {
                Run(command);
                key.Handled = true;
            }
        }

        app.Keyboard.KeyDown += Keys;

        var timer = app.AddTimeout(Refresh, () =>
        {
            if (FleetModal.Owns(claim) && !busy && Changed())
            {
                Fill();
            }

            return true;
        });

        Fill();
        Sync();

        window.Add(list, status, bar.Root);
        FleetCorners.Attach(window, Quit);

        try
        {
            app.Run(window);
        }
        finally
        {
            closed = true;
            stop.Cancel();

            if (timer is not null)
            {
                app.RemoveTimeout(timer);
            }

            FleetModal.Leave();
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }
    }
}
