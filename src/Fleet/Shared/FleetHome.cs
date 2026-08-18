namespace Fleet.Shared;

public static class FleetHome
{
    public const string OverrideVariable = "FLEET_CONFIG_HOME";

    public static string Config
    {
        get
        {
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
}
