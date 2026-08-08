namespace Fleet.Ports.Mux.Enums;

[Flags]
public enum MuxCaps
{
    None = 0,
    Split = 1 << 0,
    Zoom = 1 << 1,
    Detach = 1 << 2,
    Persist = 1 << 3,
    Popup = 1 << 4,
}
