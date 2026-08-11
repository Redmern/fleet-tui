using Fleet.Features.Projects.RemoveProject;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Shared.Keymap;
using Fleet.Shared.Keymap.Enums;
using Fleet.Ui;

namespace Fleet.Tests.Features.Projects;

public class RemoveProjectTests
{
    private static readonly Project Techweb = new("techweb", "C:/repos/techweb");

    [Fact]
    public void Removing_a_project_only_unregisters_it()
    {
        var store = new RecordingStore(Techweb);

        var removed = new RemoveProjectHandler(store).Handle(Techweb);

        Assert.True(removed.Succeeded, removed.Error);
        Assert.Equal(["techweb"], store.Removed);
        Assert.Contains("untouched", removed.Value);
        Assert.Contains(Techweb.Root, removed.Value);
    }

    [Fact]
    public void A_project_fleet_does_not_know_is_reported_rather_than_removed()
    {
        var store = new RecordingStore();

        var removed = new RemoveProjectHandler(store).Handle(Techweb);

        Assert.False(removed.Succeeded);
        Assert.Empty(store.Removed);
        Assert.Contains("not registered", removed.Error);
    }

    [Fact]
    public void A_nameless_project_is_refused_so_nothing_is_deleted_by_accident()
    {
        var store = new RecordingStore();

        var removed = new RemoveProjectHandler(store).Handle(new Project("  ", "C:/repos/x"));

        Assert.False(removed.Succeeded);
        Assert.Empty(store.Removed);
    }

    [Fact]
    public void Dropping_a_project_has_its_own_key_that_does_not_collide()
    {
        var map = Keymap.Default;

        Assert.Equal("d", KeymapDefaults.Bindings[FleetAction.RemoveProject]);
        Assert.NotEqual(map.KeyFor(FleetAction.NewProject), map.KeyFor(FleetAction.RemoveProject));
        Assert.NotEqual(map.KeyFor(FleetAction.OpenProject), map.KeyFor(FleetAction.RemoveProject));
        Assert.NotEqual(map.KeyFor(FleetAction.MoveDown), map.KeyFor(FleetAction.RemoveProject));
    }

    private sealed class RecordingStore(params Project[] known) : IProjectStore
    {
        public List<string> Removed { get; } = [];

        public Project? Load(string name) => known.FirstOrDefault(p => p.Name == name);

        public IReadOnlyList<Project> List() => known;

        public void Save(Project project) { }

        public void Remove(string name) => Removed.Add(name);
    }
}
