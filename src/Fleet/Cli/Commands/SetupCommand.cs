using Fleet.Cli.Composition;
using Fleet.Features.Setup.RunSetup;
using Fleet.Features.Setup.RunSetup.Enums;
using Fleet.Features.Setup.RunSetup.Models;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Cli.Commands;

public static class SetupCommand
{
    public static int Run()
    {
        var keymap = new Keymap(Adapters.Keymaps().Load());

        var module = Adapters.WriteKeybindModule(keymap);
        var unwired = Adapters.UnwireWezTermConfig();
        var wiring = Adapters.WireDedicatedInstance();

        var report = new SetupHandler(Adapters.OnPath)
            .Inspect(module, wiring, Adapters.ConfigDirectory);

        Print(report, keymap);
        PrintUnwireNote(unwired);

        return report.Blocked ? 1 : 0;
    }

    private static void PrintUnwireNote(UnwireResult unwired)
    {
        switch (unwired)
        {
            case UnwireResult.Removed:
                Console.WriteLine();
                Console.WriteLine(
                    "  removed fleet's old chord binding from your own wezterm config - "
                    + "fleet now runs in its own window.");
                break;

            case UnwireResult.NeedsManualRemoval:
                Console.WriteLine();
                Console.WriteLine(
                    $"  {Adapters.PersonalWezTermConfig()} still requires 'fleet' in a shape "
                    + "setup did not recognize (hand-edited?) - fleet now runs in its own "
                    + "window regardless, but remove that block yourself to fully stop it "
                    + "loading there too.");
                break;

            case UnwireResult.NothingToDo:
            default:
                break;
        }
    }

    private static void Print(SetupReport report, Keymap keymap)
    {
        Console.WriteLine("fleet setup");

        foreach (var step in report.Steps)
        {
            Console.WriteLine($"  {(step.Ok ? "ok  " : "--  ")}{step.Name,-15}{step.Detail}");
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
            Console.WriteLine("Reload wezterm, then run 'fleet' to open a project.");

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
            Console.WriteLine("Nothing above blocks fleet. Reload wezterm and run 'fleet'.");
        }
    }
}
