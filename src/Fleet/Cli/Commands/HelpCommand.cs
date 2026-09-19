namespace Fleet.Cli.Commands;

public static class HelpCommand
{
    public static int Run()
    {
        Console.WriteLine("""
            fleet - orchestration for AI coding agents

            usage:
              fleet                       pick a project and open it
              fleet dash --project <name> the dashboard (runs inside a pane)
              fleet menu                  the fleet menu, or the picker outside a project
              fleet menu --action <id>    jump straight to add-repository or keybinds
              fleet request --action <id> --project <name>
                                          hand an action to that project's dashboard
              fleet quit --project <name> close everything for that project
              fleet setup                 first run: wire wezterm, check what is missing
              fleet dispatch --project <p> "<task>"
                                          spin up a hidden sub-orchestrator for a task
              fleet apply-keybinds        write the wezterm keybinding module
              fleet doctor                check the environment
              fleet version               show the version, and check for an update
              fleet update                download and install the latest release
              fleet update --version <v>  install a specific release instead of latest (-v)
              fleet update --list         list every published release (-l)
            """);

        return 0;
    }

    public static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'. Try 'fleet --help'.");
        return 2;
    }
}
