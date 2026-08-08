using Fleet.Features.Projects.PickProject;
using Fleet.Ports.Projects;

namespace Fleet.Tests.Features.Projects;

public class PickProjectTests
{
    private sealed class StubStore(params Project[] projects) : IProjectStore
    {
        public Project? Load(string name) => projects.FirstOrDefault(p => p.Name == name);

        public IReadOnlyList<Project> List() => projects;

        public void Save(Project project) => throw new NotSupportedException();

        public void Remove(string name) => throw new NotSupportedException();
    }

    [Fact]
    public void With_no_saved_projects_only_the_new_entry_is_offered()
    {
        var entries = new PickProjectHandler(new StubStore()).Entries();

        var only = Assert.Single(entries);
        Assert.True(only.IsNew);
        Assert.Equal(PickProjectHandler.NewLabel, only.Label);
    }

    [Fact]
    public void Saved_projects_come_first_and_the_new_entry_is_always_last()
    {
        var entries = new PickProjectHandler(
            new StubStore(new Project("alpha", "/a"), new Project("beta", "/b"))).Entries();

        Assert.Equal(3, entries.Count);
        Assert.False(entries[0].IsNew);
        Assert.False(entries[1].IsNew);
        Assert.True(entries[^1].IsNew);
    }

    [Fact]
    public void A_row_shows_the_project_name_and_root()
    {
        var entries = new PickProjectHandler(
            new StubStore(new Project("backend", "/repos/backend"))).Entries();

        Assert.Contains("backend", entries[0].Label);
        Assert.Contains("/repos/backend", entries[0].Label);
    }

    [Fact]
    public void A_project_row_carries_the_project_itself()
    {
        var project = new Project("backend", "/repos/backend");

        var entries = new PickProjectHandler(new StubStore(project)).Entries();

        Assert.Same(project, entries[0].Project);
    }
}
