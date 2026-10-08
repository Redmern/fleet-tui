using Fleet.Cli.Composition;
using Fleet.Features.Setup.RunSetup;
using Fleet.Features.Setup.RunSetup.Models;
using Fleet.Ports.Keybinds.Enums;
using Fleet.Ports.Keybinds.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Cli.Commands;

public static class SetupCommand
{
    public static int Run()
    {
        var keymap = new Keymap(Adapters.Keymaps().Load());

        var report = new SetupHandler(Adapters.OnPath)
            .Inspect(Adapters.ConfigDirectory, Adapters.InspectNvim(install: true));

        var keybinds = KeybindWiring.ApplyAll();

        Print(report, keymap, keybinds);

        return report.Blocked ? 1 : 0;
    }

    private static void Print(SetupReport report, Keymap keymap, IReadOnlyList<KeybindApplied> keybinds)
    {
        Console.WriteLine("fleet setup");

        foreach (var step in report.Steps)
        {
            Console.WriteLine($"  {(step.Ok ? "ok  " : "--  ")}{step.Name,-15}{step.Detail}");
        }

        foreach (var applied in keybinds)
        {
            var ok = applied.Outcome is not KeybindOutcome.Failed;
            Console.WriteLine($"  {(ok ? "ok  " : "--  ")}{"keybinds",-15}{applied.Line}");
        }

        Console.WriteLine();
        Console.WriteLine($"  prefix chord   {keymap.PrefixDisplay}");
        Console.WriteLine(
            $"  glyph check    {FleetGlyphs.PillLeft}{FleetGlyphs.Branch} develop "
            + $"{FleetGlyphs.Ahead}1 {FleetGlyphs.Dirty}{FleetGlyphs.PillRight}");

        Console.WriteLine($"                 {SetupHints.Glyphs}");

        if (report.Missing.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Run 'fleet' to open a project.");

            return;
        }

        Console.WriteLine();
        Console.WriteLine("still to do:");

        foreach (var step in report.Missing.Where(s => s.Fix.Length > 0))
        {
            Console.WriteLine($"  {step.Name,-15}{step.Fix}");
        }

        if (!report.Blocked)
        {
            Console.WriteLine();
            Console.WriteLine("Nothing above blocks fleet. Run 'fleet'.");
        }
    }
}
