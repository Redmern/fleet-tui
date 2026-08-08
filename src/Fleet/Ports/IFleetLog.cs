namespace Fleet.Ports;

public interface IFleetLog
{
    void Swallowed(Exception e);

    void Write(string line);

    IReadOnlyList<string> Tail(int lines);
}
