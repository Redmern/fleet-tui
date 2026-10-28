namespace Fleet.Ui;

public sealed record FloatScreen(string Title, int Cols = 0, int Rows = 0, bool Lifts = false);

public static class FloatScreens
{
    private static readonly List<FloatScreen> Stack = [];

    public static FloatScreen? Current => Stack.Count > 0 ? Stack[^1] : null;

    public static Action<string> ShowTitle { get; set; } = title => Console.Title = title;

    public static Func<int, int, (int Cols, int Rows)?>? Fit { get; set; }

    public static Action? Hold { get; set; }

    public static Func<FloatScreen, (int Cols, int Rows)?>? Lift { get; set; }

    public static Func<(int Cols, int Rows)?>? Drop { get; set; }

    public static bool CanLift => Lift is not null;

    public static bool Lifted { get; private set; }

    public static (int Cols, int Rows)? Running(FloatScreen screen, bool running)
    {
        Hold?.Invoke();
        Track(screen, running);

        if (Current is not { } shown)
        {
            return null;
        }

        var fitted = Fit?.Invoke(shown.Cols, shown.Rows);
        ShowTitle(shown.Title);
        return fitted;
    }

    public static (int Cols, int Rows)? Lifting(FloatScreen screen, bool running)
    {
        Track(screen, running);

        if (Current is { Lifts: true } top && Lift?.Invoke(top) is { } lifted)
        {
            Lifted = true;
            return lifted;
        }

        if (!Lifted)
        {
            return null;
        }

        Lifted = false;
        return Drop?.Invoke();
    }

    public static void Reset()
    {
        Stack.Clear();
        Lifted = false;
    }

    private static void Track(FloatScreen screen, bool running)
    {
        if (running)
        {
            Stack.Add(screen);
        }
        else if (Stack.LastIndexOf(screen) is var at and >= 0)
        {
            Stack.RemoveAt(at);
        }
    }
}
