namespace Fleet.Ui;

public static class FleetModal
{
    private static int depth;

    public static bool Any => depth > 0;

    public static int Enter() => ++depth;

    public static void Leave() => depth--;

    public static bool Owns(int claim) => depth == claim;
}
