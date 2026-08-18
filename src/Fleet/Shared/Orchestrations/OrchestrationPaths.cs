using System.Security.Cryptography;
using System.Text;

namespace Fleet.Shared.Orchestrations;

public static class OrchestrationPaths
{
    public const string Folder = ".fleet";

    public const string Orchestrations = "orchestrations";

    public static string Root(string projectRoot) =>
        Path.Combine(projectRoot, Folder, Orchestrations);

    public static string For(string projectRoot, string slug) =>
        Path.Combine(Root(projectRoot), slug);

    public static string InstructionsFile(string folder) => Path.Combine(folder, "CLAUDE.md");

    public static string TaskFile(string folder) => Path.Combine(folder, "TASK.md");

    public static string ReportFile(string folder) => Path.Combine(folder, "REPORT.md");

    public static string ReportsFolder(string folder) => Path.Combine(folder, "reports");

    public static string ReadyMarker(string folder) =>
        Path.Combine(FleetHome.Config, "ready", Key(folder));

    private static string Key(string folder)
    {
        var normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder))
            .ToLowerInvariant();

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
