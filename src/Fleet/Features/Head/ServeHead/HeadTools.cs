using Fleet.Features.Head.ServeHead.Models;

namespace Fleet.Features.Head.ServeHead;

public static class HeadTools
{
    public const string ListProjects = "list_projects";

    public const string ListRemoteProjects = "list_remote_projects";

    public const string SwitchProject = "switch_project";

    public const string MenuAction = "menu_action";

    public const string ListAgents = "list_agents";

    public const string Relay = "relay";

    public const string Project = "project";

    public const string Action = "action";

    public const string Prompt = "prompt";

    private static readonly HeadToolParam ProjectParam =
        new(Project, "string", "The fleet project's name, as list_projects shows it.", true);

    public static IReadOnlyList<HeadToolSpec> All { get; } =
    [
        new(
            ListProjects,
            "List every fleet project: whether it is open, and how many relayed prompts wait for its orchestrator.",
            []),
        new(
            ListRemoteProjects,
            "List the projects of every machine: this machine first, then each remote machine fleet knows "
            + "(nickname and ssh host, and whether it is connected), with each project's name and whether it is "
            + "open. A remote that is not connected is listed without projects. The other head tools act on "
            + "this machine's projects only.",
            []),
        new(
            SwitchProject,
            "Show a project in the terminal, opening it first if it is not running. Hides the head.",
            [ProjectParam]),
        new(
            MenuAction,
            "Run a fleet menu action in a project's dashboard and show that project.",
            [
                ProjectParam,
                new(Action, "string", "The action id, e.g. new-agent, add-repository, keybinds.", true),
            ]),
        new(
            ListAgents,
            "List the agents of one project, or of every open project when no project is given.",
            [new(Project, "string", "The project; leave out for every open project.", false)]),
        new(
            Relay,
            "Tell a project's main orchestrator to dispatch a task: types the prompt, with the project's "
            + "dispatch trigger in front, into its Claude as if the user typed it. Opens the project if it "
            + "is closed; queues the prompt if its Claude is busy.",
            [ProjectParam, new(Prompt, "string", "The task for the orchestrator to dispatch.", true)]),
    ];

    public static IReadOnlyList<string> Names { get; } = [.. All.Select(t => t.Name)];
}
