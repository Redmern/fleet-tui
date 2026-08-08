namespace Fleet.Ports;

/// <summary>
/// Silent must not mean invisible: everything the fail-silent layer swallows
/// lands here, and `fleet doctor` reports it. Without this, a broken install is a
/// mystery rather than a diagnosis.
/// </summary>
public interface IFleetLog
{
    void Swallowed(Exception e);

    void Write(string line);

    IReadOnlyList<string> Tail(int lines);
}
