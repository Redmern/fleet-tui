namespace Fleet.Ui;

public sealed record FloatScreen(string Title, int Cols = 0, int Rows = 0);

public static class FloatScreens
{
    private static readonly List<FloatScreen> Stack = [];

    public static FloatScreen? Current => Stack.Count > 0 ? Stack[^1] : null;

    public static Action<string> ShowTitle { get; set; } = title => Console.Title = title;

    public static Func<int, int, (int Cols, int Rows)?>? Fit { get; set; }

    public static Action? Hold { get; set; }

    public static (int Cols, int Rows)? Running(FloatScreen screen, bool running)
    {
        Hold?.Invoke();

        if (running)
        {
            Stack.Add(screen);
        }
        else if (Stack.LastIndexOf(screen) is var at and >= 0)
        {
            Stack.RemoveAt(at);
        }

        if (Current is not { } shown)
        {
            return null;
        }

        var fitted = Fit?.Invoke(shown.Cols, shown.Rows);
        ShowTitle(shown.Title);
        return fitted;
    }
}
