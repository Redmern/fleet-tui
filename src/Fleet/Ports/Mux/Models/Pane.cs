namespace Fleet.Ports.Mux.Models;

public sealed record Pane(
    PaneId Id,
    string WindowId,
    string TabId,
    string SessionName,
    string Title,
    string Cwd,
    bool IsActive);
