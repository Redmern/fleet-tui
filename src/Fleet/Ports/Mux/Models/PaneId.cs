namespace Fleet.Ports.Mux.Models;

public readonly record struct PaneId(string Value)
{
    public static readonly PaneId None = new(string.Empty);

    public bool IsNone => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;
}
