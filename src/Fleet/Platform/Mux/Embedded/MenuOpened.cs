using Fleet.Shared.Viewing.Models;

namespace Fleet.Platform.Mux.Embedded;

public sealed record MenuOpened(string? Action, string? LaunchDir, ViewedFrom? From = null);
