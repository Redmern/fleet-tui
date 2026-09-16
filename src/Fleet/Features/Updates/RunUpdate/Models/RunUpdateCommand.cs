namespace Fleet.Features.Updates.RunUpdate.Models;

public sealed record RunUpdateCommand(
    string Repo, string CurrentVersion, string? PlatformAsset, string ExecutablePath);
