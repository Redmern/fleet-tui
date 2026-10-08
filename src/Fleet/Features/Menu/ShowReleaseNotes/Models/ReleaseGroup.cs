namespace Fleet.Features.Menu.ShowReleaseNotes.Models;

public sealed record ReleaseGroup(string Minor, IReadOnlyList<ReleaseEntry> Entries, bool Legacy = false);
