namespace Fleet.Features.Diagnostics.RunDoctor.Models;

public sealed record EmbeddedHealth(
    bool Linked,
    FleetdStatus? Fleetd,
    SavedSession? Saved,
    string? SavedError = null,
    bool FleetdTooOld = false);

public sealed record FleetdStatus(
    int Pid,
    string Executable,
    bool SameBuild,
    int Workspaces,
    int Panes,
    int WarmMenus,
    int Clients,
    string? Build = null);

public sealed record SavedSession(string Path, DateTime SavedAt, int Workspaces, int Panes);
