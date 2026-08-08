namespace Fleet.Features.Projects.CreateProject.Models;

public sealed record CreateProjectCommand(string Name, string Root, bool CreateRoot = false);
