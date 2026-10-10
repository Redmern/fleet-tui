using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared;
using Fleet.Shared.Iso;

namespace Fleet.Platform.Mux.Embedded.Daemon;

public static class IsoFilter
{
    public const string Failed = "failed";

    public const string NoSuchCode = "no project has that code";

    private const char KeySeparator = '|';

    private static readonly HashSet<string> Served = new(StringComparer.Ordinal)
    {
        "ping",
        "status",
        "list-projects",
        "list-workspaces",
        "list-panes",
        "list-notices",
        "dismiss-notices",
        "open-project",
        "set-label",
        "show",
        "open-window",
        "window",
        "hand-back",
    };

    public static bool IsServed(string op) => Served.Contains(op);

    public static string? Inbound(ControlRequest request, IsoCodes codes)
    {
        if (!IsServed(request.Op))
        {
            return IsoProjection.Refused;
        }

        if (!Translate(request.Workspace, codes, out var workspace)
            || !Translate(request.Session, codes, out var session))
        {
            return NoSuchCode;
        }

        request.Client = null;
        request.Workspace = workspace;
        request.Session = session;

        if (request.Op == "dismiss-notices" && workspace is { } project)
        {
            request.Args = [.. (request.Args ?? []).Select(k => NoticeKey(k, project, codes)).OfType<string>()];
        }

        return null;
    }

    public static void Outbound(ControlResponse response, IsoCodes codes)
    {
        response.Text = null;
        response.Remotes = null;

        if (!response.Ok)
        {
            response.Error = Failed;
        }

        if (response.Status is { } status)
        {
            status.Executable = string.Empty;
            status.SessionFile = null;
            status.Host = null;
        }

        foreach (var pane in response.Panes ?? [])
        {
            pane.Session = codes.Workspace(pane.Session);
            pane.Window = codes.Workspace(pane.Window);
            pane.Title = string.Empty;
            pane.PaneTitle = string.Empty;
            pane.Cwd = string.Empty;
        }

        foreach (var workspace in response.Workspaces ?? [])
        {
            workspace.Name = codes.Workspace(workspace.Name);
        }

        foreach (var entry in response.Window ?? [])
        {
            entry.Name = codes.Workspace(entry.Name);
            entry.Host = null;
        }

        if (response.Projects is { } projects)
        {
            response.Projects = [.. projects.Select(codes.Project)];
        }

        foreach (var notice in response.Notices ?? [])
        {
            var agent = codes.Agent(notice.Project, notice.Worktree);
            notice.Key = $"{notice.Kind}{KeySeparator}{agent}";
            notice.Agent = agent;
            notice.Message = IsoProjection.Notice(notice.Kind);
            notice.Worktree = string.Empty;
            notice.Project = codes.Project(notice.Project);
            notice.Host = null;
            notice.Machine = null;
        }
    }

    private static bool Translate(string? value, IsoCodes codes, out string? translated)
    {
        if (value is null)
        {
            translated = null;
            return true;
        }

        translated = codes.WorkspaceNamed(value);
        return translated is not null;
    }

    private static string? NoticeKey(string key, string project, IsoCodes codes) =>
        key.Split(KeySeparator, 2) is [var kind, var agent] && codes.WorktreeOf(project, agent) is { } worktree
            ? $"{kind}{KeySeparator}{PathKey.For(worktree)}"
            : null;
}
