namespace Fleet.Platform.Forwards;

public static class Backoff
{
    private const int MostDoublings = 6;

    public static TimeSpan After(TimeSpan first, TimeSpan most, int failures)
    {
        var wait = first * Math.Pow(2, Math.Clamp(failures - 1, 0, MostDoublings));
        return wait < most ? wait : most;
    }
}
