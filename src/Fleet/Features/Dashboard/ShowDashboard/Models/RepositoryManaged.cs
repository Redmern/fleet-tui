using Fleet.Shared.Keymap.Enums;

namespace Fleet.Features.Dashboard.ShowDashboard.Models;

public sealed record RepositoryManaged(string? Status, FleetAction Follow = FleetAction.None)
{
    public static readonly RepositoryManaged Nothing = new(Status: null);

    public static RepositoryManaged Then(FleetAction action) => new(Status: null, action);
}
