namespace Fleet.Ports.Releases.Models;

public sealed record CachedUpdateCheck(string Latest, DateTimeOffset CheckedAt);
