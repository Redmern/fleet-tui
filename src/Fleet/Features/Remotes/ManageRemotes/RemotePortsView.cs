using System.Globalization;
using Fleet.Ports.Browser;
using Fleet.Ports.Forwards;
using Fleet.Ports.Forwards.Enums;
using Fleet.Ports.Forwards.Models;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Remotes.ManageRemotes;

public static class RemotePortsView
{
    public const string EmptyHint = "(nothing listens on a port above 1023 there yet; f forwards a port by number)";

    public const string Pitfalls =
        "binds 127.0.0.1 only · Vite allowedHosts, HMR clientPort, shared cookies and docker -p 0.0.0.0 can bite";

    public static void Show(
        IApplication app, Keymap keymap, IPortForwards forwards, IBrowserLauncher browser, string host, string described)
    {
        var window = FleetTheme.Overlay($"ports of {described}", 90, 16 + FleetCorners.Rows);
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));
        var status = FleetTheme.StatusLine(Pos.AnchorEnd(2));
        var bar = new FleetActionBar(Pos.AnchorEnd(1));
        FleetKeys.ApplyMotions(list, keymap);
        status.Text = Pitfalls;

        IReadOnlyList<PortForward> shown = [];
        var busy = 0;

        void Fill(IReadOnlyList<PortForward> rows)
        {
            shown = rows;
            FleetRows.Fill(list, Rows(rows), FleetRows.Selected(list));
        }

        void Reload() =>
            _ = Task.Run(async () =>
            {
                var rows = Of(await forwards.ListAsync().ConfigureAwait(false), host);
                app.Invoke(() => Fill(rows));
            });

        PortForward? Selected()
        {
            var index = FleetRows.Selected(list);
            return index >= 0 && index < shown.Count ? shown[index] : null;
        }

        void Run(string doing, Func<Task<string>> work)
        {
            if (Interlocked.Exchange(ref busy, 1) != 0)
            {
                return;
            }

            status.Text = doing;
            _ = Task.Run(async () =>
            {
                string said;
                try
                {
                    said = await work().ConfigureAwait(false);
                }
                catch (MuxUnavailableException e)
                {
                    said = e.Message;
                }
                finally
                {
                    Interlocked.Exchange(ref busy, 0);
                }

                app.Invoke(() => status.Text = said);
                Reload();
            });
        }

        void Forward()
        {
            var port = Selected() is { State: ForwardState.Detected or ForwardState.Failed or ForwardState.Waiting } row
                ? row.RemotePort
                : AskPort(app, $"Forward a port of {described}");
            if (port is not { } remote)
            {
                return;
            }

            Run($"forwarding {remote}...", async () =>
                (await forwards.AddAsync(host, remote).ConfigureAwait(false)) is var added && added.Url is { } url
                    ? $"{remote} is at {url}"
                    : added.Describe());
        }

        void Unforward()
        {
            if (Selected() is not { State: ForwardState.Forwarded or ForwardState.Waiting or ForwardState.Failed } row)
            {
                return;
            }

            Run($"stopping {row.RemotePort}...", async () =>
            {
                await forwards.RemoveAsync(host, row.RemotePort).ConfigureAwait(false);
                return $"stopped forwarding {row.RemotePort}";
            });
        }

        void Open()
        {
            if (Selected() is not { } row)
            {
                return;
            }

            if (row.Url is null)
            {
                status.Text = $"{row.RemotePort} is not forwarded; f forwards it";
                return;
            }

            Run($"opening {row.Url}...", async () =>
                await ForwardOpening.OpenAsync(forwards, browser, row).ConfigureAwait(false) is { } failed
                    ? failed
                    : $"opened {row.Url}");
        }

        list.Accepting += (_, e) =>
        {
            Open();
            e.Handled = true;
        };

        bar.Show(
        [
            ("enter", FleetIcons.OpenBrowser, Open),
            ("f", FleetIcons.Forward, Forward),
            ("u", FleetIcons.Unforward, Unforward),
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

            if (key == FleetKeys.Cancel || key == Key.Q)
            {
                app.RequestStop(window);
            }
            else if (key == Key.F)
            {
                Forward();
            }
            else if (key == Key.U)
            {
                Unforward();
            }
            else if (key == Key.O)
            {
                Open();
            }
            else
            {
                return;
            }

            key.Handled = true;
        }

        app.Keyboard.KeyDown += Keys;
        var ticking = app.AddTimeout(TimeSpan.FromSeconds(1), () =>
        {
            Reload();
            return true;
        });

        window.Add(list, status, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));
        Fill([]);
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

    public static IReadOnlyList<PortForward> Of(IReadOnlyList<PortForward> all, string host) =>
        [.. all.Where(f => !f.Viewer && string.Equals(f.Host, host, StringComparison.OrdinalIgnoreCase))];

    public static int Forwarded(IReadOnlyList<PortForward> all, string host) =>
        Of(all, host).Count(f => f.State == ForwardState.Forwarded);

    public static int? AskPort(IApplication app, string title) =>
        FleetDialog.Ask(app, title, "remote port, for example 5173") is { } typed
        && int.TryParse(typed.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
        && port is > 0 and <= 65535
            ? port
            : null;

    public static IReadOnlyList<FleetRow> Rows(IReadOnlyList<PortForward> rows)
    {
        if (rows.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint)];
        }

        return [.. rows.Select(r =>
        {
            var (mark, tone, state) = r.State switch
            {
                ForwardState.Forwarded => ("●", FleetTones.Good, r.Url!),
                ForwardState.Waiting => ("…", FleetTones.Muted, r.Error ?? "waiting"),
                ForwardState.Failed => ("✗", FleetTones.Bad, r.Error ?? "failed"),
                _ => ("○", FleetTones.Muted, "listening · f forwards it"),
            };

            return new FleetRow(
                [
                    new FleetSpan($" {mark} ", tone),
                    FleetSpan.Plain(r.RemotePort.ToString(CultureInfo.InvariantCulture).PadRight(6)),
                    FleetSpan.Muted(r.Project is { } project ? $"  {project}" : string.Empty),
                ],
                [FleetSpan.Muted($"{state} ")]);
        })];
    }
}
