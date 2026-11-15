namespace Fleet.Shared;

public static class FleetHome
{
    public const string OverrideVariable = "FLEET_CONFIG_HOME";

    private static readonly AsyncLocal<string?> Scoped = new();

    public static string Config
    {
        get
        {
            if (Scoped.Value is { } scoped)
            {
                return scoped;
            }

            var over = Environment.GetEnvironmentVariable(OverrideVariable);

            if (!string.IsNullOrWhiteSpace(over))
            {
                return over;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "fleet");
        }
    }

    public static IDisposable Scope(string path)
    {
        var outer = Scoped.Value;
        Scoped.Value = path;
        return new Restore(outer);
    }

    private sealed class Restore(string? outer) : IDisposable
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
