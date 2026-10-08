namespace Fleet.Features.Menu.ShowReleaseNotes.Models;

public sealed record ReleaseEntry(string Version, string? Date, IReadOnlyList<string> Bullets);
