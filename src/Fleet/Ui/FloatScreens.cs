namespace Fleet.Ui;

public sealed record FloatScreen(string Title, int Cols = 0, int Rows = 0);

public static class FloatScreens
{
    private static readonly List<FloatScreen> Stack = [];

    public static FloatScreen? Current => Stack.Count > 0 ? Stack[^1] : null;

    public static Action<string> ShowTitle { get; set; } = title => Console.Title = title;

    public static Action<int, int>? Fit { get; set; }

    public static void Running(FloatScreen screen, bool running)
    {
        if (running)
        {
            Stack.Add(screen);
        }
        else if (Stack.LastIndexOf(screen) is var at and >= 0)
        {
            Stack.RemoveAt(at);
        }

        if (Current is { } shown)
        {
            ShowTitle(shown.Title);
            Fit?.Invoke(shown.Cols, shown.Rows);
        }
    }
}