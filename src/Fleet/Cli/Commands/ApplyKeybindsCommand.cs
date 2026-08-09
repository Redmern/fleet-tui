using Fleet.Cli.Composition;
using Fleet.Ui;

namespace Fleet.Cli.Commands;

public static class ApplyKeybindsCommand
{
    public static int Run()
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

        return 0;
    }
}
