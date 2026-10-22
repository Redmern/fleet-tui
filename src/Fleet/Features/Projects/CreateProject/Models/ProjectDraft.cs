namespace Fleet.Features.Projects.CreateProject.Models;

public sealed record ProjectDraft(string Name, string Root)
{
    public static ProjectDraft Opening(ProjectDraft? draft, string? defaultRoot) =>
        draft ?? new ProjectDraft(string.Empty, defaultRoot ?? string.Empty);

    public ProjectDraft Browsed(string? chosen) =>
        chosen is { Length: > 0 } ? this with { Root = chosen } : this;
}
