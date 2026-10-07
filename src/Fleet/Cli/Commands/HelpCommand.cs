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
              fleet head [--voice]        the head orchestrator's Claude (alt+o opens it in voice mode)
              fleet mcp --head            the head's cross-project MCP tools over stdio
              fleet doctor                check the environment
              fleet theme list            the themes (built-in and <fleet config>\themes\*.toml)
              fleet theme get             the active theme
              fleet theme set <name>      switch theme; running fleet windows follow live
              fleet theme sync            follow omarchy's current theme
              fleet theme install omarchy hook omarchy's theme-set so fleet follows every switch
              fleet version               show the version, and check for an update
              fleet update                download and install the latest release
              fleet update <v>            install a specific release instead of latest
              fleet update --version <v>  the same, as a flag (-v)
              fleet update --list         list every published release (-l)

            embedded multiplexer (the default outside wezterm and tmux; FLEET_MUX=wezterm opts out):
              fleet attach [--project <p>] attach this terminal to fleetd (a picker when it runs several projects)
              fleet attach --ssh <host>   attach to fleetd on another machine over ssh
              fleet daemon                run fleetd in the foreground
              fleet daemon stop           stop fleetd and forget its projects (the next start opens only what you open)
              fleet bridge                ssh's remote end: pipe stdio to the local fleetd
              prefix is ctrl+s (FLEET_PREFIX; keys in <fleet config>\embedded-keys.json), and it shows
              the keys: h/j/k/l focus, arrows resize, % " split, c new tab, n/p/1-9 tabs, z zoom,
              x/& close pane/tab, o next pane, s switch project, space menu, [ copy, ] paste,
              f/t/e/g floats, r reload keys, d detach; ctrl+s again sends ctrl+s
              without prefix: ctrl+h/j/k/l move focus, alt+h/j/k/l resize (nvim gets both), alt+left/right tabs,
              ctrl+enter menu, shift+enter newline for claude, alt+o the head (voice)
            """);

        return 0;
    }

    public static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'. Try 'fleet --help'.");
        return 2;
    }
}
