using Fleet.Features.Todos.ShowBoard.Enums;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
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
        string project,
        string projectRoot,
        BoardTickets board,
        Func<string, Task<Result>> open)
    {
        var window = FleetTheme.Overlay(BoardRows.Title(project));

        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));
        var status = FleetTheme.Caption(1, Pos.AnchorEnd(2), string.Empty);

        FleetKeys.ApplyMotions(list, keymap);

        IReadOnlyList<Ticket> tickets = [];
        var shown = string.Empty;
        var opening = false;

        Ticket? Selected() =>
            tickets.Count == 0 ? null : tickets[Math.Clamp(FleetRows.Selected(list), 0, tickets.Count - 1)];

        void Fill(string? keep = null)
        {
            var at = FleetRows.Selected(list);
            keep ??= Selected()?.Id;

            tickets = board.List(projectRoot);

            var rows = BoardRows.For(tickets);
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
            string.Join('\n', BoardRows.For(board.List(projectRoot)).Select(r => r.Text)) != shown;

        void Apply(Result<Ticket> result, Func<Ticket, string> message, bool follow)
        {
            status.Text = result.Succeeded ? message(result.Value) : result.Error!;
            Fill(result.Succeeded && follow ? result.Value.Id : string.Empty);
        }

        async Task OpenAsync(string id)
        {
            var opened = await BoardRows.SafelyAsync(() => open(id)).ConfigureAwait(false);

            await FleetAsync.OnUi(app, () =>
            {
                opening = false;

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
                return;
            }

            if (Selected() is not { } ticket || opening)
            {
                return;
            }

            switch (command)
            {
                case BoardCommand.Open:
                    opening = true;
                    status.Text = $"Opening {ticket.Id}...";
                    _ = OpenAsync(ticket.Id);
                    break;

                case BoardCommand.Effort:
                    Apply(board.CycleEffort(projectRoot, ticket.Id), BoardRows.EffortSaved, follow: true);
                    break;

                case BoardCommand.Close:
                    Apply(board.Close(projectRoot, ticket.Id), t => $"Closed {t.Id}. The file stays in .fleet/todos.", follow: false);
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
            if (FleetModal.Owns(claim) && !opening && Changed())
            {
                Fill();
            }

            return true;
        });

        Fill();

        window.Add(list, status, bar.Root);
        FleetCorners.Attach(window, Quit);

        try
        {
            app.Run(window);
        }
        finally
        {
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
