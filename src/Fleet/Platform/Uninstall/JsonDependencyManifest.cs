using System.Text.Json;
using Fleet.Platform.Uninstall.Models;
using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Platform.Uninstall;

public sealed class JsonDependencyManifest(string configHome) : IDependencyManifest
{
    public const string FileName = "installed-deps.json";

    private const int SupportedVersion = 1;

    public IReadOnlyList<InstalledDependency> Read()
    {
        var manifest = Load(Path.Combine(configHome, FileName));

        if (manifest?.Version != SupportedVersion || manifest.Deps is null)
        {
            return [];
        }

        return [.. manifest.Deps.Select(ToDependency).OfType<InstalledDependency>()];
    }

    private static DependencyManifestJson? Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), UninstallJsonContext.Default.DependencyManifestJson)
                : null;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private static InstalledDependency? ToDependency(DependencyEntryJson? entry)
    {
        if (entry is not { Package: { Length: > 0 } package, Sudo: { } sudo, InstalledAt: { } installedAt }
            || ToolOf(entry.Tool) is not { } tool
            || ManagerOf(entry.Manager) is not { } manager)
        {
            return null;
        }

        return new(tool, manager, package, sudo, installedAt);
    }

    private static DependencyTool? ToolOf(string? name) => name switch
    {
        "git" => DependencyTool.Git,
        "nvim" => DependencyTool.Nvim,
        "yazi" => DependencyTool.Yazi,
        _ => null,
    };

    private static PackageManager? ManagerOf(string? name) => name switch
    {
        "brew" => PackageManager.Brew,
        "pacman" => PackageManager.Pacman,
        "dnf" => PackageManager.Dnf,
        "apt" => PackageManager.Apt,
        "winget" => PackageManager.Winget,
        _ => null,
    };
}
