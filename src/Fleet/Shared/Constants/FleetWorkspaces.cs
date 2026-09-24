namespace Fleet.Shared.Constants;

public static class FleetWorkspaces
{
    public const string Default = "default";

    public const string Hidden = "fleet-hidden";

    public const string HiddenSuffix = "~hidden";

    public static string HiddenFor(string project) => project + HiddenSuffix;

    public static bool IsHidden(string workspace) =>
        workspace.EndsWith(HiddenSuffix, StringComparison.OrdinalIgnoreCase)
        || string.Equals(workspace, Hidden, StringComparison.OrdinalIgnoreCase);
}
