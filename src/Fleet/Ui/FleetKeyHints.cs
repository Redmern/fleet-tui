using Fleet.Shared.Keymap.Enums;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace Fleet.Ui;

public static class FleetKeyHints
{
    public static readonly TimeSpan FirstRepeat = TimeSpan.FromMilliseconds(1100);

    public static readonly TimeSpan ShortestLapse = TimeSpan.FromMilliseconds(150);

    public const int RepeatsMissed = 4;

    public static readonly TimeSpan Recheck = TimeSpan.FromSeconds(1);

    private static bool setting = true;

    private static bool toggled;

    private static bool held;

    private static long heldAt;

    private static long interval;

    private static bool releases;

    public static event Action? Changed;

    public static bool Capturing { get; set; }

    public static bool Shown => setting || toggled || held;

    public static bool Setting => setting;

    public static void Apply(bool showKeys)
    {
        var before = Shown;
        toggled = toggled && showKeys == setting;
        setting = showKeys;
        Raise(before);
    }

    public static void Toggle()
    {
        var before = Shown;
        toggled = !setting && !toggled;
        Raise(before);
    }

    public static void Hold(long now)
    {
        var before = Shown;
        interval = held ? now - heldAt : 0;
        held = true;
        heldAt = now;
        Raise(before);
    }

    public static void Release()
    {
        var before = Shown;
        held = false;
        interval = 0;
        Raise(before);
    }

    public static void Released()
    {
        releases = true;
        Release();
    }

    public static TimeSpan Window => interval > 0
        ? TimeSpan.FromMilliseconds(Math.Max(ShortestLapse.TotalMilliseconds, interval * RepeatsMissed))
        : FirstRepeat;

    public static bool Lapsed(long now) =>
        held && !releases && now - heldAt >= (long)Window.TotalMilliseconds;

    public static void Reset()
    {
        (setting, toggled, held, heldAt, interval, releases) = (true, false, false, 0, 0, false);
        Capturing = false;
        Changed = null;
    }

    public static void Attach(IApplication app, Func<Keymap> keys, Func<bool> load)
    {
        var keymap = keys();
        Apply(load());

        app.Keyboard.KeyDown += (_, key) =>
        {
            if (key.Handled || Typing(app))
            {
                return;
            }

            if (key == keymap.KeyFor(FleetAction.RevealMenuKeys))
            {
                Toggle();
                key.Handled = true;
            }
            else if (key == keymap.KeyFor(FleetAction.HoldMenuKeys))
            {
                Hold(Environment.TickCount64);
                app.AddTimeout(Window + TimeSpan.FromMilliseconds(20), () =>
                {
                    if (Lapsed(Environment.TickCount64))
                    {
                        Release();
                    }

                    return false;
                });
                key.Handled = true;
            }
        };

        if (app.Driver is { } driver)
        {
            driver.KeyUp += (_, key) =>
            {
                if (Same(key, keymap.KeyFor(FleetAction.HoldMenuKeys)))
                {
                    Released();
                }
            };
        }

        app.AddTimeout(Recheck, () =>
        {
            keymap = keys();
            Apply(load());
            return true;
        });
    }

    private static bool Same(Key pressed, Key bound) =>
        bound.IsValid && (pressed == bound || (bound.AsRune.Value != 0 && pressed.AsRune == bound.AsRune));

    private static bool Typing(IApplication app) =>
        Capturing || app.Navigation?.GetFocused() is TextField;

    private static void Raise(bool before)
    {
        if (before != Shown)
        {
            Changed?.Invoke();
        }
    }
}
