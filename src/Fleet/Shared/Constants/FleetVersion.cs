using System.Reflection;

namespace Fleet.Shared.Constants;

public static class FleetVersion
{
    public static string Current { get; } = Format(Assembly.GetExecutingAssembly().GetName().Version);

    public static string Format(Version? version) =>
        version is null ? "0.0.0"
        : version.Revision > 0 ? version.ToString(4)
        : version.ToString(3);
}
