namespace Fleet.Ports.Sessions.Models;

public sealed record WindowSession(string Name, IReadOnlyList<SessionProject> Projects, SessionProject? Showing = null);