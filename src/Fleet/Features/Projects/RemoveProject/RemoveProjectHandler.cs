using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Results;

namespace Fleet.Features.Projects.RemoveProject;

public sealed class RemoveProjectHandler(IProjectStore store)
{
    public Result<string> Handle(Project project)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
        {
            return Result<string>.Fail("that project has no name to unregister");
        }

        if (store.Load(project.Name) is null)
        {
            return Result<string>.Fail($"{project.Name} is not registered with fleet");
        }

        store.Remove(project.Name);

        return Result<string>.Ok(
            $"{project.Name} is no longer registered. {project.Root} is untouched.");
    }
}
