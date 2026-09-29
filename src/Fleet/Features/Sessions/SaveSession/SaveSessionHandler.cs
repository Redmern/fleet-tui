using Fleet.Ports.Sessions;
using Fleet.Ports.Sessions.Models;
using Fleet.Shared;
using Fleet.Shared.Results;

namespace Fleet.Features.Sessions.SaveSession;

public sealed record WindowEntry(string Project, string? Host, bool Shown);

public sealed class SaveSessionHandler(ISessionStore store)
{
    public static WindowSession For(string name, IReadOnlyList<WindowEntry> window) =>
        new(
            name.Trim(),
            [.. window.Select(e => new SessionProject(e.Project, e.Host))],
            window.FirstOrDefault(e => e.Shown) is { } shown ? new SessionProject(shown.Project, shown.Host) : null);

    public Result<WindowSession> Handle(string name, IReadOnlyList<WindowEntry> window)
    {
        if (window.Count == 0)
        {
            return Result<WindowSession>.Fail("this window has no projects to save");
        }

        if (ProjectName.Sanitize(name.Trim()).Length == 0)
        {
            return Result<WindowSession>.Fail("a session needs a name");
        }

        var session = For(name, window);
        store.Save(session);
        return Result<WindowSession>.Ok(session);
    }

    public static string Describe(WindowSession session) =>
        string.Join(", ", session.Projects.Select(p => p.Host is null ? p.Name : $"{p.Name} @{p.Host}"));
}