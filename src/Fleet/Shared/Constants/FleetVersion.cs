using System.Reflection;

namespace Fleet.Shared.Constants;

public static class FleetVersion
{
    public static string Current { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";
}
