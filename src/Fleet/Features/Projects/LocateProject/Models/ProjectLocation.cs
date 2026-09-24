namespace Fleet.Features.Projects.LocateProject.Models;

public sealed record ProjectLocation(bool Open, bool ShownHere)
{
    public static readonly ProjectLocation Closed = new(false, false);
}
