namespace Fleet.Features.Projects.CreateProject.Models;

public sealed record ProjectDraft(string Name, string Root)
{
    public ProjectDraft Browsed(string? chosen) =>
        chosen is { Length: > 0 } ? this with { Root = chosen } : this;
}
