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
    public const string EmptyHint = "(no remote machines; n connects one)";

    public static readonly TimeSpan Patience = TimeSpan.FromMinutes(3);

    public static void Show(IApplication app, Keymap keymap, IRemoteMachines remotes)
    {
        var window = FleetTheme.Overlay("remote machines", 90, 16);
        var list = FleetTheme.Rows(1, 1, Dim.Fill(3));
        var status = FleetTheme.StatusLine(Pos.AnchorEnd(2));
        var bar = new FleetActionBar(Pos.AnchorEnd(1));
        FleetKeys.ApplyMotions(list, keymap);

        IReadOnlyList<RemoteMachine> shown = [];
        var busy = 0;

        void Fill(IReadOnlyList<RemoteMachine> machines)
        {
            shown = machines;
            FleetRows.Fill(list, Rows(machines), FleetRows.Selected(list));
        }

        void Reload() =>
            _ = Task.Run(async () =>
            {
                var machines = await remotes.ListAsync().ConfigureAwait(false);
                app.Invoke(() => Fill(machines));
            });

        RemoteMachine? Selected()
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

            status.Text = $"connecting to {host.Trim()}...";
            Run(() => ConnectFlowAsync(app, remotes, host.Trim(), line => app.Invoke(() => status.Text = line)));
        }

        void Answer()
        {
            if (Selected() is not { } machine)
            {
                return;
            }

            if (machine.State is RemoteState.Asking or RemoteState.Failed)
            {
                status.Text = $"connecting to {machine.Host}...";
                Run(() => ConnectFlowAsync(app, remotes, machine.Host, line => app.Invoke(() => status.Text = line)));
            }
        }

        void Disconnect()
        {
            if (Selected() is not { } machine
                || !FleetDialog.Confirm(app, $"Disconnect {machine.Name}?", ["Its projects leave your list; they keep running there."], "Disconnect"))
            {
                return;
            }

            Run(() => remotes.DisconnectAsync(machine.Host));
        }

        list.Accepting += (_, e) =>
        {
            Answer();
            e.Handled = true;
        };

        bar.Show(
        [
            ("n", "connect", Connect),
            ("enter", "answer / retry", Answer),
            ("d", "disconnect", Disconnect),
            ("q/esc", "close", () => app.RequestStop(window)),
        ]);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
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

    public static async Task ConnectFlowAsync(IApplication app, IRemoteMachines remotes, string host, Action<string> say)
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
                    say($"connected to {machine.Name}: {Projects(machine.Projects.Count)}");
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

    public static IReadOnlyList<FleetRow> Rows(IReadOnlyList<RemoteMachine> machines)
    {
        if (machines.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint)];
        }

        var widest = machines.Max(m => m.Name.Length);

        return [.. machines.Select(m =>
        {
            var (mark, tone, state) = m.State switch
            {
                RemoteState.Connected => ("●", FleetTones.Good, Projects(m.Projects.Count)),
                RemoteState.Asking => ("?", FleetTones.Warn, "waiting for your answer: press enter"),
                RemoteState.Failed => ("✗", FleetTones.Bad, m.Error ?? "could not connect"),
                _ => ("…", FleetTones.Muted, "connecting"),
            };

            return new FleetRow(
                [new FleetSpan($" {mark} ", tone), FleetSpan.Plain(m.Name.PadRight(widest)), FleetSpan.Muted($"  {m.Host}")],
                [FleetSpan.Muted($"{state} ")]);
        })];
    }

    private static string Projects(int count) => count == 1 ? "1 project" : $"{count} projects";
}
