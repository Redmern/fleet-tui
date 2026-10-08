namespace Fleet.Features.Updates.CheckUpdate.Models;

public sealed record UpdateCheck(bool UpdateAvailable, string? Latest, string? Error)
{
    public string? Notice(string currentVersion) =>
        UpdateAvailable && Latest is not null
            ? $"You are on v{currentVersion.TrimStart('v', 'V')}. {Latest} is available."
            : null;
}
