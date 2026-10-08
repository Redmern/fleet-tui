namespace Fleet.Features.Updates.RunUpdate.Models;

public sealed record UpdateOutcome(bool Installed, string Message, string? Version = null);
