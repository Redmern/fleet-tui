namespace Fleet.Ports.Orchestrations;

public interface IDispatchHistory
{
    IReadOnlyList<string> List(string project);

    void Add(string project, string prompt);
}
