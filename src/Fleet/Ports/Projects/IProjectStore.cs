namespace Fleet.Ports.Projects;

/// <summary>
/// A named root folder whose children are repositories. Multi-repo is the
/// default case, not a feature added later.
/// </summary>
public sealed record Project(string Name, string Root);

public interface IProjectStore
{
    /// <summary>Returns null for an unknown or unreadable project rather than throwing.</summary>
    Project? Load(string name);

    /// <summary>Sorted by name. Skips unreadable entries rather than failing the whole listing.</summary>
    IReadOnlyList<Project> List();

    void Save(Project project);

    void Remove(string name);
}
