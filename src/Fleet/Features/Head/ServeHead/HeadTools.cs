using Fleet.Features.Head.ServeHead.Models;

namespace Fleet.Features.Head.ServeHead;

public static class HeadTools
{
    public const string ListProjects = "list_projects";

    public const string ListRemoteProjects = "list_remote_projects";

    public const string SwitchProject = "switch_project";

    public const string MenuAction = "menu_action";

    public const string ListAgents = "list_agents";

    public const string ProjectStructure = "project_structure";

    public const string Relay = "relay";

    public const string Tell = "tell";

    public const string ShowAgent = "show_agent";

    public const string HideAgent = "hide_agent";

    public const string Project = "project";

    public const string Repository = "repository";

    public const string Branch = "branch";

    public const string Sub = "sub";

    public const string Action = "action";

    public const string Prompt = "prompt";

    public const string ListRemotes = "list_remotes";

    public const string Remote = "remote";

    public const string Local = "local";

    public const string ListForwards = "list_forwards";

    public const string ForwardPort = "forward_port";

    public const string UnforwardPort = "unforward_port";

    public const string OpenUrl = "open_url";

    public const string StartStack = "start_stack";

    public const string StopStack = "stop_stack";

    public const string Port = "port";

    public const string LocalPort = "local_port";

    public const string Open = "open";

    private static readonly HeadToolParam ForwardRemoteParam =
        new(Remote, "string", "The remote machine's nickname, as list_remotes shows it (or its ssh host).", true);

    private static readonly HeadToolParam PortParam =
        new(Port, "string", "The port the web app listens on, on the remote machine.", true);

    private static readonly HeadToolParam ProjectParam =
        new(Project, "string", "The fleet project's name, as list_projects shows it.", true);

    private static readonly HeadToolParam RemoteParam =
        new(
            Remote,
            "string",
            $"The machine's nickname, as list_remotes shows it. Leave out (or '{Local}') for the machine fleet "
            + "was opened on. fleet connects a remote that is not connected yet.",
            false);

    public static IReadOnlyList<HeadToolSpec> All { get; } =
    [
        new(
            ListRemotes,
            $"List the machines the head can act on: '{Local}' (the machine fleet was opened on) first, then each "
            + "remote machine fleet knows, with its nickname, ssh host, when it last connected and whether it is "
            + "connected now. Pass a nickname as 'remote' to the other tools.",
            []),
        new(
            ListProjects,
            "List the fleet projects of one machine: whether each is open, and how many relayed prompts wait for "
            + "its orchestrator. Each row names the machine it is on.",
            [RemoteParam]),
        new(
            ListRemoteProjects,
            "List the projects of every machine: this machine first, then each remote machine fleet knows "
            + "(nickname and ssh host, and whether it is connected), with each project's name and whether it is "
            + "open. A remote that is not connected is listed without projects.",
            []),
        new(
            SwitchProject,
            "Show a project in the terminal, opening it first if it is not running. Hides the head.",
            [ProjectParam, RemoteParam]),
        new(
            MenuAction,
            "Run a fleet menu action in a project's dashboard and show that project.",
            [
                ProjectParam,
                new(Action, "string", "The action id, e.g. new-agent, add-repository, keybinds.", true),
                RemoteParam,
            ]),
        new(
            ListAgents,
            "List the agents of one project, or of every open project when no project is given.",
            [new(Project, "string", "The project; leave out for every open project.", false), RemoteParam]),
        new(
            ProjectStructure,
            "Show one project's full structure: its repositories, its sub-orchestrators each with the agents it "
            + "started (status and last report), and the agents that are not under any sub-orchestrator.",
            [ProjectParam, RemoteParam]),
        new(
            Tell,
            "Pass a plain message to a project's main orchestrator: a question (\"what is the status?\"), a "
            + "follow-up or an instruction. Types the prompt as-is into its Claude as if the user typed it, never "
            + "as a dispatch. Use this for everything except an explicit dispatch. Opens the project if it is "
            + "closed; queues the prompt if its Claude is busy. When that Claude takes cross-session messages, "
            + "types nothing and answers with an address: send the message there with SendMessage.",
            [ProjectParam, new(Prompt, "string", "The message for the orchestrator, as the user worded it.", true), RemoteParam]),
        new(
            Relay,
            "Have a project's main orchestrator dispatch a new sub-orchestrator: types the prompt, with the "
            + "project's dispatch trigger in front, into its Claude. Use only when the user explicitly asks to "
            + "dispatch a task; use tell for anything else. Opens the project if it is closed; queues the prompt "
            + "if its Claude is busy. When that Claude takes cross-session messages, types nothing and answers "
            + "with an address and a dispatch request: send it there with SendMessage.",
            [ProjectParam, new(Prompt, "string", "The task for the orchestrator to dispatch.", true), RemoteParam]),
        new(
            ShowAgent,
            "Show the pane of one agent or sub-orchestrator in its project's window, by name. Starts it if it is "
            + "not running and opens the project if it is closed. Showing a visible pane does nothing.",
            TargetParams),
        new(
            HideAgent,
            "Hide the pane of one agent or sub-orchestrator by name, without stopping it. Hiding a hidden pane "
            + "does nothing.",
            TargetParams),
        new(
            ListForwards,
            "List the web app ports of the connected remote machines: each forwarded port with its localhost URL, "
            + "and the listening ports that are not forwarded. Ports a project lists in forwardPorts are forwarded "
            + "automatically while it runs.",
            []),
        new(
            ForwardPort,
            "Forward a web app port of a remote machine to localhost on this machine over fleet's ssh link (bound to "
            + "127.0.0.1 only). Uses the same local port when free, else another; the answer names the URL.",
            [
                ForwardRemoteParam,
                PortParam,
                new(LocalPort, "string", "The local port to use instead of the same number.", false),
            ]),
        new(UnforwardPort, "Stop forwarding a remote machine's port.", [ForwardRemoteParam, PortParam]),
        new(
            OpenUrl,
            "Open a forwarded port in the browser on this machine.",
            [
                new(Port, "string", "The remote port (or the local port) of the forward.", true),
                new(Remote, "string", "The remote machine's nickname; leave out when only one forward has that port.", false),
            ]),
        new(
            StartStack,
            "Run a remote project's runCommand in a pane there, wait until its readyPort (or first forwardPorts "
            + "entry) listens and answers, forward it and give the URL.",
            [
                ForwardRemoteParam,
                ProjectParam,
                new(Open, "string", "'true' to open the URL in the browser once it is up.", false),
            ]),
        new(StopStack, "Stop the stack start_stack started for a remote project.", [ForwardRemoteParam, ProjectParam]),
    ];

    private static IReadOnlyList<HeadToolParam> TargetParams =>
    [
        ProjectParam,
        new(
            Repository,
            "string",
            "The agent's repository, as list_agents shows it. Give it with 'branch'; leave both out for a "
            + "sub-orchestrator.",
            false),
        new(Branch, "string", "The agent's branch, as list_agents shows it.", false),
        new(
            Sub,
            "string",
            "A sub-orchestrator's name. Alone it names that sub-orchestrator; with 'repository' and 'branch' it "
            + "names an agent that sub-orchestrator started.",
            false),
        RemoteParam,
    ];

    public static IReadOnlyList<string> Names { get; } = [.. All.Select(t => t.Name)];
}
