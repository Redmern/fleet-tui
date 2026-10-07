using Fleet.Shared.Settings.Enums;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FloatBorder
{
    public static readonly IReadOnlyList<string> Sends =
        ["f1", "f2", "f3", "f4", "f5", "f6", "f7", "f8", "f9", "f10", "f11", "f12"];

    private static readonly Key[] Keys =
        [Key.F1, Key.F2, Key.F3, Key.F4, Key.F5, Key.F6, Key.F7, Key.F8, Key.F9, Key.F10, Key.F11, Key.F12];

    private static readonly List<View> Running = [];

    private static readonly Dictionary<View, Action?> Cornered = [];

    private static readonly List<FleetActionBar> Bars = [];

    private static Func<IReadOnlyList<FloatBorderButton>, bool>? publish;

    private static IReadOnlyList<FloatBorderButton> published = [];

    private static bool framedOnly;

    public static bool Enabled => publish is not null && !framedOnly;

    public static bool Framing => publish is not null && framedOnly;

    public static bool Enable(IApplication app, Func<IReadOnlyList<FloatBorderButton>, bool> sink, bool framed = false)
    {
        if (!Enable(sink, framed))
        {
            return false;
        }

        app.Keyboard.KeyDown += (_, key) =>
        {
            if (!key.Handled && Press(key))
            {
                key.Handled = true;
            }
        };

        return true;
    }

    public static bool Enable(Func<IReadOnlyList<FloatBorderButton>, bool> sink, bool framed = false)
    {
        if (publish is not null)
        {
            return true;
        }

        if (!sink([]))
        {
            return false;
        }

        publish = sink;
        framedOnly = framed;
        FleetKeyHints.Changed += Refresh;
        FleetButtonHints.Changed += Refresh;
        return true;
    }

    public static void Reset()
    {
        FleetKeyHints.Changed -= Refresh;
        FleetButtonHints.Changed -= Refresh;
        publish = null;
        published = [];
        framedOnly = false;
        Running.Clear();
        Cornered.Clear();
        Bars.Clear();
    }

    public static void Run(View window, bool running)
    {
        if (running)
        {
            Running.Add(window);
        }
        else if (Running.LastIndexOf(window) is var at and >= 0)
        {
            Running.RemoveAt(at);
        }

        Refresh();
    }

    public static void Corners(View window, Action? close)
    {
        Cornered[window] = close;

        foreach (var bar in Bars.Where(b => Inside(b.Root, window)))
        {
            bar.Root.Visible = false;
        }

        Refresh();
    }

    public static void Track(FleetActionBar bar) => Bars.Add(bar);

    public static void Forget(View root) => Bars.RemoveAll(b => b.Root == root);

    public static void Refresh()
    {
        if (publish is null)
        {
            return;
        }

        var buttons = Current().Buttons;

        if (buttons.SequenceEqual(published) || !publish(buttons))
        {
            return;
        }

        published = buttons;
    }

    public static bool Press(Key key)
    {
        if (publish is null || Array.IndexOf(Keys, key) is not (var at and >= 0))
        {
            return false;
        }

        var actions = Current().Actions;

        if (at >= actions.Count)
        {
            return false;
        }

        actions[at]();
        return true;
    }

    public static (IReadOnlyList<FloatBorderButton> Buttons, IReadOnlyList<Action> Actions) Current()
    {
        if (Running.Count == 0)
        {
            return ([], []);
        }

        var window = Running[^1];

        if (framedOnly && !Cornered.ContainsKey(window))
        {
            return ([], []);
        }

        var bar = Bars.LastOrDefault(b => b.Items.Count > 0 && Inside(b.Root, window));
        var items = bar?.Items ?? [];
        var cornered = Cornered.TryGetValue(window, out var close);

        var buttons = For(
            cornered,
            items,
            bar?.AlignRight ?? false,
            bar?.Pinned,
            FleetKeyHints.Shown,
            FleetKeyHints.RevealKey,
            FleetButtonHints.Mode,
            close is not null);

        var actions = new List<Action>();

        if (cornered)
        {
            actions.Add(FleetKeyHints.Toggle);
        }

        if (close is not null)
        {
            actions.Add(close);
        }

        actions.AddRange(items.Select(i => i.Run));
        return (buttons, actions);
    }

    public static IReadOnlyList<FloatBorderButton> For(
        bool corners,
        IReadOnlyList<(string Key, string Label, Action Run)> bar,
        bool alignRight,
        string? pinned,
        bool keysShown,
        string revealKey,
        ButtonHints hints = ButtonHints.Tooltips,
        bool closable = true)
    {
        var buttons = new List<FloatBorderButton>();

        if (corners)
        {
            buttons.Add(new FloatBorderButton(
                false,
                false,
                keysShown ? string.Empty : revealKey,
                FleetButtonHints.Face(FleetIcons.Info, hints),
                Send(0),
                Tip(FleetIcons.Info, hints)));
        }

        if (corners && closable)
        {
            buttons.Add(new FloatBorderButton(
                false,
                true,
                keysShown ? FleetCorners.CloseKey : string.Empty,
                FleetButtonHints.Face(FleetIcons.Close, hints),
                Send(1),
                Tip(FleetIcons.Close, hints)));
        }

        var shown = FleetActionBar.Visible(bar, keysShown, pinned);

        foreach (var chip in shown)
        {
            buttons.Add(new FloatBorderButton(
                true, alignRight, chip.Key, FleetButtonHints.Face(chip.Label, hints), Send(buttons.Count), Tip(chip.Label, hints)));
        }

        return buttons;
    }

    private static string Tip(string icon, ButtonHints hints) =>
        hints == ButtonHints.Tooltips ? FleetIcons.Name(icon) : string.Empty;

    private static string Send(int index) => index < Sends.Count ? Sends[index] : string.Empty;

    private static bool Inside(View view, View window)
    {
        for (var at = view.SuperView; at is not null; at = at.SuperView)
        {
            if (at == window)
            {
                return true;
            }
        }

        return false;
    }
}
