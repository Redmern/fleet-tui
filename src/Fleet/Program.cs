namespace Fleet;

/// <summary>
/// The composition root: the only file permitted to name a Fleet.Platform type.
/// Slices are wired to each other here and nowhere else.
///
/// Scaffolding for now — commands are added as their slices land.
/// </summary>
public static class Program
{
    public static Task<int> Main(string[] args) => Task.FromResult(
        (args.Length > 0 ? args[0] : "") switch
        {
            "--help" or "-h" or "help" => Help(),
            "" => Help(),
            var verb => Unknown(verb),
        });

    private static int Help()
    {
        Console.WriteLine("""
            fleet — orchestration for AI coding agents

            usage:
              fleet                       pick a project and open it
              fleet dash --project <name> the dashboard (runs inside a pane)
              fleet doctor                check the environment

            Nothing is wired up yet: see docs/PHASE1-PLAN.md.
            """);
        return 0;
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'. Try 'fleet --help'.");
        return 2;
    }
}
