using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Ports.Themes.Enums;
using Fleet.Shared.Results;
using Fleet.Shared.Themes;

namespace Fleet.Cli.Commands;

public static class ThemeCommand
{
    private const string Usage = "usage: fleet theme list | get | set <name> | sync | install omarchy";

    public static int Run(Invocation invocation)
    {
        var args = invocation.Arguments ?? [];
        var sub = args.Count > 0 ? args[0] : "get";
        var rest = string.Join(' ', args.Skip(1)).Trim();
        var themes = Adapters.Themes();

        switch (sub)
        {
            case "list":
                var active = themes.Active().Name;

                foreach (var theme in themes.List())
                {
                    var mark = theme.Name == active ? "*" : " ";
                    var custom = themes.IsCustom(theme) ? "  (custom)" : string.Empty;

                    Console.WriteLine($"{mark} {theme.Name,-20} {theme.Title}{custom}");
                }

                return 0;

            case "get":
                Console.WriteLine(themes.Active().Name);
                return 0;

            case "set" when rest.Length > 0:
                return Applied(themes.Set(rest));

            case "sync":
                return Applied(themes.SyncOmarchy());

            case "install" when rest == "omarchy":
                var setup = themes.InstallOmarchy(Adapters.Executable);

                Console.WriteLine(setup.Hook switch
                {
                    HookInstall.Created => $"created {setup.HookFile}",
                    HookInstall.Appended => $"added fleet to {setup.HookFile}",
                    HookInstall.Updated => $"pointed {setup.HookFile} at this fleet",
                    _ => $"{setup.HookFile} already runs fleet",
                });

                return setup.Theme is { } synced
                    ? Report(synced)
                    : Failed(setup.SyncError ?? "could not read omarchy's theme");

            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    private static int Applied(Result<ThemePalette> result) =>
        result.Succeeded ? Report(result.Value) : Failed(result.Error!);

    private static int Report(ThemePalette theme)
    {
        Console.WriteLine($"theme: {theme.Name} ({theme.Title})");

        if (Adapters.ApplyWezTermTheme(theme) is { } lua)
        {
            Console.WriteLine($"  wezterm  wrote {lua}; {Adapters.TouchWezTermConfig()}");
        }

        return 0;
    }

    private static int Failed(string error)
    {
        Console.Error.WriteLine($"theme: {error}");
        return 1;
    }
}
