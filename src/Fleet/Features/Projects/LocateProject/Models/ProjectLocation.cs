namespace Fleet.Features.Projects.LocateProject.Models;

public sealed record ProjectLocation(bool Open, bool ShownHere, bool InWindow = false, bool InOtherWindow = false)
{
    public static readonly ProjectLocation Closed = new(false, false);
}
