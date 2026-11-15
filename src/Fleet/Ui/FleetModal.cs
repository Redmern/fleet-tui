namespace Fleet.Ui;

public static class FleetModal
{
    private static readonly Depth Process = new();

    private static readonly AsyncLocal<Depth?> Scoped = new();

    private static Depth Current => Scoped.Value ?? Process;

    public static bool Any => Current.Open > 0;

    public static IDisposable Scope()
    {
        var outer = Scoped.Value;
        Scoped.Value = new Depth();
        return new Restore(outer);
    }

    public static int Enter()
    {
        var current = Current;
        current.BackFrom = 0;
        return ++current.Open;
    }

    public static void Leave()
    {
        var current = Current;
        if (current.BackFrom > current.Open)
        {
            current.BackFrom = 0;
        }

        current.Open--;
    }

    public static bool Owns(int claim) => Current.Open == claim;

    public static void Back()
    {
        var current = Current;
        current.BackFrom = current.Open;
    }

    public static bool WentBack()
    {
        var current = Current;
        var went = current.BackFrom == current.Open + 1;
        current.BackFrom = 0;
        return went;
    }

    private sealed class Depth
    {
        public int Open;

        public int BackFrom;
    }

    private sealed class Restore(Depth? outer) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            Scoped.Value = outer;
        }
    }
}
