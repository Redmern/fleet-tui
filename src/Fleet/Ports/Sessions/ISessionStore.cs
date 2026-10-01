using Fleet.Ports.Sessions.Models;

namespace Fleet.Ports.Sessions;

public interface ISessionStore
{
    IReadOnlyList<WindowSession> List();

    void Save(WindowSession session);

    bool Remove(string name);
}