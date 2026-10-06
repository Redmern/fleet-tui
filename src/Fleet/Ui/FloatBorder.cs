using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FloatBorder
{
    public const string RevealSend = "f1";

    private static readonly List<View> Running = [];

    private static readonly HashSet<View> Cornered = [];

    private static readonly List<FleetActionBar> Bars = [];

    private static Func<IReadOnlyList<FloatBorderButton>, bool>? publish;

    private static IReadOnlyList<FloatBorderButton> published = [];

    public static bool Enabled => publish is not null;

    public static bool Enable(Func<IReadOnlyList<FloatBorderButton>, bool> sink)
    {
        if (!sink([]))
        {
            return false;
        }

        publish = sink;
        FleetKeyHints.Changed += Refresh;
        return true;
    }

    public static void Reset()
    {
        publish = null;
        published = [];
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

    public static void Corners(View window)
    {
        Cornered.Add(window);
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

        var buttons = Current();

        if (buttons.SequenceEqual(published))
        {
            return;
        }

        published = buttons;
        publish(buttons);
    }

    public static IReadOnlyList<FloatBorderButton> Current()
    {
        if (Running.Count == 0)
        {
            return [];
        }

        var window = Running[^1];
        var bar = Bars.LastOrDefault(b => b.Items.Count > 0 && Inside(b.Root, window));

        return For(
            Cornered.Contains(window),
            bar?.Items ?? [],
            bar?.AlignRight ?? false,
            bar?.Pinned,
            FleetKeyHints.Shown,
            FleetKeyHints.RevealKey);
    }

    public static IReadOnlyList<FloatBorderButton> For(
        bool corners,
        IReadOnlyList<(string Key, string Label, Action Run)> bar,
        bool alignRight,
        string? pinned,
        bool keysShown,
        string revealKey)
    {
        var buttons = new List<FloatBorderButton>();

        if (corners)
        {
            buttons.Add(new FloatBorderButton(false, false, keysShown ? string.Empty : revealKey, FleetIcons.Info, RevealSend));
            buttons.Add(new FloatBorderButton(
                false, true, keysShown ? FleetCorners.CloseKey : string.Empty, FleetIcons.Close, FleetCorners.CloseKey));
        }

        var shown = FleetActionBar.Visible(bar, keysShown, pinned);

        for (var i = 0; i < bar.Count; i++)
        {
            buttons.Add(new FloatBorderButton(true, alignRight, shown[i].Key, shown[i].Label, Send(bar[i].Key)));
        }

        return buttons;
    }

    public static string Send(string key)
    {
        if (key.Length > 1 && key.Contains('/'))
        {
            return Send(key.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty);
        }

        return key.ToLowerInvariant() switch
        {
            "shift" => "shift+enter",
            "bksp" => "backspace",
            _ => key,
        };
    }

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
