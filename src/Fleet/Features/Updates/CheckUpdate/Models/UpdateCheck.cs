namespace Fleet.Features.Updates.CheckUpdate.Models;

public sealed record UpdateCheck(bool UpdateAvailable, string? Latest, string? Error);
