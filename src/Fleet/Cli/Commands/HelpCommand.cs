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
              fleet update <v>            install a specific release instead of latest
              fleet update --version <v>  the same, as a flag (-v)
              fleet update --list         list every published release (-l)

            embedded multiplexer (FLEET_MUX=embedded, or no wezterm/tmux):
              fleet attach [--project <p>] attach this terminal to fleetd, starting it if needed
              fleet attach --ssh <host>   attach to fleetd on another machine over ssh
              fleet daemon                run fleetd in the foreground
              fleet bridge                ssh's remote end: pipe stdio to the local fleetd
              prefix is ctrl+b (FLEET_PREFIX): q detach, space menu, n/p tab, s next project,
              h/j/k/l focus, r redraw, f new float, w show/hide floats, e float/tile the pane,
              g float mode (hjkl/arrows move, HJKL/shift+arrows size, esc done),
              [ copy mode (hjkl/arrows, g/G, ctrl+u/d, v select, y copy, q quit), wheel scrolls,
              ctrl+b again sends ctrl+b
            """);

        return 0;
    }

    public static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'. Try 'fleet --help'.");
        return 2;
    }
}
