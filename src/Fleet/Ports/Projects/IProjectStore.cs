using Fleet.Ports.Projects.Models;

namespace Fleet.Ports.Projects;

public interface IProjectStore
{
    Project? Load(string name);

    IReadOnlyList<Project> List();

    void Save(Project project);

    void Remove(string name);
}
