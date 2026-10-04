namespace Fleet.Ui;

public static class FleetModal
{
    private static int depth;

    private static int backFrom;

    public static bool Any => depth > 0;

    public static int Enter()
    {
        backFrom = 0;
        return ++depth;
    }

    public static void Leave()
    {
        if (backFrom > depth)
        {
            backFrom = 0;
        }

        depth--;
    }

    public static bool Owns(int claim) => depth == claim;

    public static void Back() => backFrom = depth;

    public static bool WentBack()
    {
        var went = backFrom == depth + 1;
        backFrom = 0;
        return went;
    }
}
