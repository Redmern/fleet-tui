using Fleet.Cli.Composition;
using Fleet.Cli.Models;
using Fleet.Features.Setup.RunSetup;
using Fleet.Features.Setup.RunSetup.Models;
using Fleet.Features.Setup.WriteInclude.Enums;
using Fleet.Ports.Keybinds.Enums;
using Fleet.Ports.Keybinds.Models;
using Fleet.Shared.Results;
using Fleet.Ui;
using Fleet.Ui.Constants;

namespace Fleet.Cli.Commands;

public static class SetupCommand
{
    public static int Run(Invocation invocation)
    {
        if (invocation.Arguments?.Contains(IncludeWiring.IncludeOnlyFlag) == true)
        {
            return IncludeOnly();
        }

        var keymap = new Keymap(Adapters.Keymaps().Load());

        var report = new SetupHandler(Adapters.OnPathOrEditor)
            .Inspect(
                Adapters.ConfigDirectory,
                Adapters.InspectNvim(install: true),
                Adapters.ResolvedEditor(string.Empty),
                [.. Adapters.HarnessesInUse().Keys]);

        var keybinds = KeybindWiring.ApplyAll();
        var include = IncludeWiring.Write();

        Print(report, keymap, keybinds, include);

        return report.Blocked ? 1 : 0;
    }

    private static int IncludeOnly()
    {
        var include = IncludeWiring.Write();

        Console.WriteLine($"fleet: {IncludeLine(include)}");

        return include.Succeeded ? 0 : 1;
    }

    private static string IncludeLine(Result<IncludeWrite> include) =>
        include switch
        {
            { Succeeded: false } => include.Error!,
            { Value: IncludeWrite.Written } => $"wrote {IncludeWiring.IncludePath}; fleet doctor says how to import it",
            _ => $"{IncludeWiring.IncludePath} is current; fleet doctor says how to import it",
        };

    private static void Print(
        SetupReport report, Keymap keymap, IReadOnlyList<KeybindApplied> keybinds, Result<IncludeWrite> include)
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

        Console.WriteLine($"  {(include.Succeeded ? "ok  " : "--  ")}{"orchestrator",-15}{IncludeLine(include)}");

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
