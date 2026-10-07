using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Ports.Keybinds.Enums;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;
using Fleet.Ui;

namespace Fleet.Cli.Commands;

public static class ApplyKeybindsCommand
{
    public const string TargetFlag = "--target";

    public const string DryRunFlag = "--dry-run";

    private const string Usage = "usage: fleet apply-keybinds [--target nvim|claude] [--dry-run]";

    public static int Run(Invocation invocation)
    {
        var args = invocation.Arguments ?? [];
        var dryRun = args.Contains(DryRunFlag);
        var at = args.ToList().IndexOf(TargetFlag);
        IReadOnlyList<KeybindTarget> only = KeybindWiring.Rendered;

        if (args.Any(a => a.StartsWith("--", StringComparison.Ordinal) && a is not TargetFlag and not DryRunFlag))
        {
            Console.Error.WriteLine(Usage);
            return 2;
        }

        if (at >= 0)
        {
            if (at + 1 >= args.Count
                || !KeybindNames.TryTarget(args[at + 1], out var target)
                || !KeybindWiring.Rendered.Contains(target))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            only = [target];
        }

        if (at < 0 && !dryRun)
        {
            WriteWezTerm();
            Console.WriteLine();
        }

        Console.WriteLine(dryRun ? "keybinds (dry run, nothing written)" : "keybinds");

        var applied = KeybindWiring.Apply(only, dryRun);

        foreach (var line in applied)
        {
            Console.WriteLine($"  {line.Line}");
        }

        return applied.Any(a => a.Outcome == KeybindOutcome.Failed) ? 1 : 0;
    }

    private static void WriteWezTerm()
    {
        var keymap = new Keymap(Adapters.Keymaps().Load());

        var target = Adapters.WriteKeybindModule(keymap);

        Console.WriteLine($"wrote {target}");
        Console.WriteLine($"  prefix chord  {keymap.PrefixDisplay}");
        Console.WriteLine($"  reload        {Adapters.TouchWezTermConfig()}");
        Console.WriteLine();
        Console.WriteLine("add these two lines to your .wezterm.lua, then reload wezterm:");
        Console.WriteLine();
        Console.WriteLine("  local fleet = require 'fleet'");
        Console.WriteLine("  fleet.apply(config)");
        Console.WriteLine();
        Console.WriteLine("wezterm must be able to find fleet.lua, so ensure ~/.wezterm is on");
        Console.WriteLine("package.path, or copy fleet.lua next to your .wezterm.lua.");
    }
}
