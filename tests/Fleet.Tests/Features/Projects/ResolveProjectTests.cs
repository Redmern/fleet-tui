using Fleet.Features.Projects.ResolveProject;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;

namespace Fleet.Tests.Features.Projects;

public class ResolveProjectTests
{
    private static string Root(params string[] parts) =>
        Path.Combine([Path.GetPathRoot(Environment.CurrentDirectory) ?? "/", .. parts]);

    [Fact]
    public void The_project_whose_root_contains_the_directory_wins()
    {
        var handler = new ResolveProjectHandler(
            new StubStore(new Project("backend", Root("repos", "backend"))));

        var resolved = handler.ForDirectory(Root("repos", "backend", "widgets", "main"));

        Assert.NotNull(resolved);
        Assert.Equal("backend", resolved!.Name);
    }

    [Fact]
    public void The_root_itself_resolves()
    {
        var handler = new ResolveProjectHandler(
            new StubStore(new Project("backend", Root("repos", "backend"))));

        Assert.NotNull(handler.ForDirectory(Root("repos", "backend")));
    }

    [Fact]
    public void An_unrelated_directory_resolves_to_nothing()
    {
        var handler = new ResolveProjectHandler(
            new StubStore(new Project("backend", Root("repos", "backend"))));

        Assert.Null(handler.ForDirectory(Root("somewhere", "else")));
    }

    [Fact]
    public void The_most_specific_root_wins_when_projects_nest()
    {
        var handler = new ResolveProjectHandler(new StubStore(
            new Project("outer", Root("repos")),
            new Project("inner", Root("repos", "backend"))));

        var resolved = handler.ForDirectory(Root("repos", "backend", "widgets"));

        Assert.Equal("inner", resolved!.Name);
    }

    [Fact]
    public void A_sibling_with_a_shared_prefix_is_not_a_match()
    {
        var handler = new ResolveProjectHandler(
            new StubStore(new Project("backend", Root("repos", "backend"))));

        Assert.Null(handler.ForDirectory(Root("repos", "backend-tools")));
    }

    [Fact]
    public void No_projects_resolves_to_nothing()
        => Assert.Null(new ResolveProjectHandler(new StubStore()).ForDirectory(Root("repos")));

    [Fact]
    public void A_blank_directory_resolves_to_nothing()
        => Assert.Null(new ResolveProjectHandler(new StubStore()).ForDirectory("   "));

    private sealed class StubStore(params Project[] projects) : IProjectStore
    {
        public Project? Load(string name) => projects.FirstOrDefault(p => p.Name == name);

        public IReadOnlyList<Project> List() => projects;

        public void Save(Project project) => throw new NotSupportedException();

        public void Remove(string name) => throw new NotSupportedException();
    }
}
