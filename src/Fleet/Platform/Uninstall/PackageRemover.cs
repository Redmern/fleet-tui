using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Platform.Uninstall;

public sealed partial class PackageRemover(bool interactive, Func<string[], int> run) : IPackageRemover
{
    public PackageRemover(bool interactive)
        : this(interactive, RunInheritingTerminal)
    {
    }

    public static string[]? CommandFor(InstalledDependency dependency)
    {
        if (!SafePackage().IsMatch(dependency.Package))
        {
            return null;
        }

        string[] command = dependency.Manager switch
        {
            PackageManager.Brew => ["brew", "uninstall", dependency.Package],
            PackageManager.Pacman => ["pacman", "-R", "--noconfirm", dependency.Package],
            PackageManager.Dnf => ["dnf", "remove", "-y", dependency.Package],
            PackageManager.Apt => ["apt-get", "remove", "-y", dependency.Package],
            PackageManager.Winget => ["winget", "uninstall", "--id", dependency.Package, "-e"],
            _ => [],
        };

        if (command.Length == 0)
        {
            return null;
        }

        return dependency.Sudo ? ["sudo", .. command] : command;
    }

    public DependencyRemoval Remove(InstalledDependency dependency)
    {
        if (CommandFor(dependency) is not { } command)
        {
            return new(false, $"refusing the package name '{dependency.Package}'");
        }

        if (dependency.Sudo && !interactive)
        {
            return new(false, "it needs sudo and stdin is not a terminal");
        }

        try
        {
            var exit = run(command);

            return exit == 0
                ? new(true, string.Join(' ', command))
                : new(false, $"{command[dependency.Sudo ? 1 : 0]} exited with code {exit}");
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            return new(false, e.Message);
        }
    }

    private static int RunInheritingTerminal(string[] command)
    {
        var start = new ProcessStartInfo(command[0]) { UseShellExecute = false };

        foreach (var argument in command.Skip(1))
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"{command[0]} did not start");

        process.WaitForExit();

        return process.ExitCode;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._+@-]{0,127}\z")]
    private static partial Regex SafePackage();
}
