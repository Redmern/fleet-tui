using Fleet.Features.Remotes.ManageRemotes.Models;
using Fleet.Ports.Remotes;
using Fleet.Ports.Remotes.Enums;
using Fleet.Ports.Remotes.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Features.Remotes.ManageRemotes;

public static class ManageRemotesView
{
    public const string EmptyHint = "(no remote machines yet; n connects one and fleet remembers it)";

    public const string KnownDetail = "known · not connected";

    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    public static void Show(IApplication app, Keymap keymap, IRemoteMachines remotes, IKnownRemoteStore known)
    {
        var window = FleetTheme.Overlay("remote machines", 90, 16);
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));
        var status = FleetTheme.StatusLine(Pos.AnchorEnd(2));
        var bar = new FleetActionBar(Pos.AnchorEnd(1));
        FleetKeys.ApplyMotions(list, keymap);

        IReadOnlyList<RemoteEntry> shown = [];
        var busy = 0;

        void Fill(IReadOnlyList<RemoteEntry> entries)
        {
            shown = entries;
            FleetRows.Fill(list, Rows(entries), FleetRows.Selected(list));
        }

        void Reload() =>
            _ = Task.Run(async () =>
            {
                var machines = await remotes.ListAsync().ConfigureAwait(false);
                var remembered = known.Load();

                foreach (var machine in machines.Where(m => m.State == RemoteState.Connected
                    && !remembered.Any(k => string.Equals(k.Host, m.Host, StringComparison.OrdinalIgnoreCase))))
                {
                    known.Remember(machine.Host, DateTimeOffset.Now);
                    remembered = known.Load();
                }

                var entries = RemoteEntry.Merge(machines, remembered);
                app.Invoke(() => Fill(entries));
            });

        RemoteEntry? Selected()
        {
            var index = FleetRows.Selected(list);
            return index >= 0 && index < shown.Count ? shown[index] : null;
        }

        void Run(Func<Task> work)
        {
            if (Interlocked.Exchange(ref busy, 1) != 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await work().ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref busy, 0);
                    Reload();
                }
            });
        }

        void Connect()
        {
            var host = FleetDialog.Ask(app, "Connect a remote machine", "ssh host, for example user@homelab");
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }

            ConnectTo(host.Trim());
        }

        void ConnectTo(string host)
        {
            status.Text = $"connecting to {host}...";
            Run(() => ConnectFlowAsync(app, remotes, known, host, line => app.Invoke(() => status.Text = line)));
        }

        void Answer()
        {
            if (Selected() is not { } entry)
            {
                return;
            }

            if (entry.Live is null or { State: RemoteState.Asking or RemoteState.Failed })
            {
                ConnectTo(entry.Host);
            }
        }

        void Disconnect()
        {
            if (Selected() is not { Live: not null } entry
                || !FleetDialog.Confirm(app, $"Disconnect {entry.Described}?", ["Its projects leave your list; they keep running there."], "Disconnect"))
            {
                return;
            }

            Run(() => remotes.DisconnectAsync(entry.Host));
        }

        void Rename()
        {
            if (Selected() is not { } entry)
            {
                return;
            }

            if (entry.Known is null)
            {
                if (entry.Live is not { State: RemoteState.Connected })
                {
                    status.Text = $"connect {entry.Host} first; then it can have a nickname";
                    return;
                }

                known.Remember(entry.Host, DateTimeOffset.Now);
            }

            var nickname = FleetDialog.Ask(
                app,
                "Rename remote machine",
                $"Nickname for {entry.Host} (empty clears it)",
                initial: entry.Known?.Nickname ?? string.Empty);
            if (nickname is null)
            {
                return;
            }

            known.Rename(entry.Host, nickname);
            status.Text = string.IsNullOrWhiteSpace(nickname) ? $"{entry.Host} has no nickname" : $"{entry.Host} is now {nickname.Trim()}";
            Reload();
        }

        void Forget()
        {
            if (Selected() is not { } entry)
            {
                return;
            }

            if (entry.Known is null)
            {
                status.Text = $"{entry.Host} is not remembered";
                return;
            }

            string[] lines = entry.Live is null
                ? ["fleet stops listing it here; n connects it again."]
                : ["fleet stops remembering it; it stays connected until you disconnect it."];
            if (!FleetDialog.Confirm(app, $"Forget {entry.Described}?", lines, "Forget"))
            {
                return;
            }

            known.Forget(entry.Host);
            status.Text = $"forgot {entry.Host}";
            Reload();
        }

        list.Accepting += (_, e) =>
        {
            Answer();
            e.Handled = true;
        };

        bar.Show(
        [
            ("n", FleetIcons.Connect, Connect),
            ("enter", FleetIcons.Answer, Answer),
            ("e", FleetIcons.Rename, Rename),
            ("x", FleetIcons.Forget, Forget),
            ("d", FleetIcons.Disconnect, Disconnect),
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
            else if (key == Key.N)
            {
                Connect();
            }
            else if (key == Key.D)
            {
                Disconnect();
            }
            else if (key == Key.E)
            {
                Rename();
            }
            else if (key == Key.X)
            {
                Forget();
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

    public static IReadOnlyList<string> ConnectAll(
        IApplication app, IRemoteMachines remotes, IKnownRemoteStore known, IReadOnlyList<string> hosts)
    {
        var window = FleetTheme.Overlay("connecting remote machines", 70, 6);
        var status = FleetTheme.StatusLine(1);
        window.Add(status);
        var failures = new List<string>();

        _ = Task.Run(async () =>
        {
            try
            {
                foreach (var host in hosts)
                {
                    await ConnectFlowAsync(app, remotes, known, host, line => app.Invoke(() => status.Text = line)).ConfigureAwait(false);

                    var machine = (await remotes.ListAsync().ConfigureAwait(false))
                        .FirstOrDefault(m => string.Equals(m.Host, host, StringComparison.OrdinalIgnoreCase));

                    if (machine is not { State: RemoteState.Connected })
                    {
                        failures.Add($"{host}: {machine?.Error ?? "not connected"}; the session opens without it");
                    }
                }
            }
            finally
            {
                app.Invoke(() => app.RequestStop(window));
            }
        });

        status.Text = $"connecting to {string.Join(", ", hosts)}...";
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

        return failures;
    }

    public static async Task ConnectFlowAsync(
        IApplication app, IRemoteMachines remotes, IKnownRemoteStore known, string host, Action<string> say)
    {
        await remotes.ConnectAsync(host).ConfigureAwait(false);
        string? answered = null;
        var waited = System.Diagnostics.Stopwatch.StartNew();

        while (waited.Elapsed < Patience)
        {
            var machine = (await remotes.ListAsync().ConfigureAwait(false))
                .FirstOrDefault(m => string.Equals(m.Host, host, StringComparison.OrdinalIgnoreCase));

            switch (machine)
            {
                case null:
                    return;

                case { State: RemoteState.Connected }:
                    known.Remember(machine.Host, DateTimeOffset.Now);
                    say($"connected to {machine.Label}: {Projects(machine.Projects.Count)}");
                    return;

                case { State: RemoteState.Failed }:
                    say($"{host}: {machine.Error ?? "could not connect"}");
                    return;

                case { State: RemoteState.Asking, Prompt: { } prompt }:
                    var question = prompt == answered ? $"{prompt}\n(that was not accepted; try again)" : prompt;
                    var answer = await FleetAsync.OnUi(app, () => FleetDialog.Ask(app, host, question, machine.Secret))
                        .ConfigureAwait(false);

                    if (answer is null)
                    {
                        await remotes.DisconnectAsync(host).ConfigureAwait(false);
                        say($"cancelled {host}");
                        return;
                    }

                    await remotes.AnswerAsync(host, answer).ConfigureAwait(false);
                    answered = prompt;
                    say($"connecting to {host}...");
                    await Task.Delay(TimeSpan.FromMilliseconds(600)).ConfigureAwait(false);
                    continue;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
        }

        say($"{host}: still connecting; check it here later");
    }

    public static IReadOnlyList<FleetRow> Rows(IReadOnlyList<RemoteEntry> entries)
    {
        if (entries.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint)];
        }

        var widest = entries.Max(e => e.Label.Length);

        return [.. entries.Select(e =>
        {
            var host = string.Equals(e.Label, e.Host, StringComparison.OrdinalIgnoreCase) ? string.Empty : $"  {e.Host}";

            if (e.Live is not { } m)
            {
                return new FleetRow(
                    [FleetSpan.Muted(" ○ "), FleetSpan.Muted(e.Label.PadRight(widest)), FleetSpan.Muted(host)],
                    [FleetSpan.Muted($"{KnownDetail} ")]);
            }

            var (mark, tone, state) = m.State switch
            {
                RemoteState.Connected => ("●", FleetTones.Good, Projects(m.Projects.Count)),
                RemoteState.Asking => ("?", FleetTones.Warn, "waiting for your answer: press enter"),
                RemoteState.Failed => ("✗", FleetTones.Bad, m.Error ?? "could not connect"),
                _ => ("…", FleetTones.Muted, "connecting"),
            };

            return new FleetRow(
                [new FleetSpan($" {mark} ", tone), FleetSpan.Plain(e.Label.PadRight(widest)), FleetSpan.Muted(host)],
                [FleetSpan.Muted($"{state} ")]);
        })];
    }

    private static string Projects(int count) => count == 1 ? "1 project" : $"{count} projects";
}
