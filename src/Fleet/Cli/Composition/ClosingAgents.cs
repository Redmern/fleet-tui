using Fleet.Ports.Agents;
using Fleet.Ports.Projects;
using Fleet.Shared.Status;

namespace Fleet.Cli.Composition;

public static class ClosingAgents
{
    public static void MarkAll(IProjectStore projects, IAgentStore agents)
    {
        foreach (var project in projects.List())
        {
            MarkProject(agents, project.Name);
        }
    }

    public static void MarkProject(IAgentStore agents, string project)
    {
        foreach (var agent in agents.List(project).Where(a => a.Open))
        {
            ClosingMark.Write(agent.Worktree);
        }
    }
}
