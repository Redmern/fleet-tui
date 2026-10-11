using Fleet.Features.Todos.AddTodo;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;
using Fleet.Ports.Todos;
using Fleet.Shared.Results;
using Fleet.Shared.Todos.Models;

namespace Fleet.Tests.Features.Todos;

public class CaptureTodoTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    private static readonly Project Techweb = new("techweb", "/work/techweb");

    private static readonly Project FleetProject = new("fleet", "/work/fleet");

    private readonly RootRecordingStore _todos = new();

    private CaptureTodo Capture => new(_todos, new FakeProjects(Techweb, FleetProject));

    [Fact]
    public void Text_is_filed_under_the_active_project()
    {
        var captured = Capture.Handle("add oauth login\nmore detail", Techweb, Now);

        Assert.True(captured.Succeeded, captured.Error);
        var (root, ticket) = Assert.Single(_todos.Added);
        Assert.Equal(Techweb.Root, root);
        Assert.Equal("add oauth login", ticket.Title);
        Assert.Equal("fleet: added todo 0001-add-oauth-login to techweb", captured.Value.Note);
    }

    [Fact]
    public void A_project_prefix_files_the_ticket_under_that_project()
    {
        var captured = Capture.Handle("fleet: board view", Techweb, Now);

        var (root, ticket) = Assert.Single(_todos.Added);
        Assert.Equal(FleetProject.Root, root);
        Assert.Equal("board view", ticket.Title);
        Assert.Equal("fleet", captured.Value.Project.Name);
    }

    [Fact]
    public void A_prefix_works_without_an_active_project()
    {
        Assert.True(Capture.Handle("techweb: idea", null, Now).Succeeded);
        Assert.Equal(Techweb.Root, _todos.Added.Single().Root);
    }

    [Fact]
    public void Without_an_active_project_or_a_prefix_nothing_is_filed_and_the_note_says_why()
    {
        var captured = Capture.Handle("idea", null, Now);

        Assert.False(captured.Succeeded);
        Assert.Contains("project:", captured.Error);
        Assert.Empty(_todos.Added);
    }

    private sealed class RootRecordingStore : ITodoStore
    {
        public List<(string Root, Ticket Ticket)> Added { get; } = [];

        public IReadOnlyList<Ticket> List(string projectRoot) => [];

        public Ticket? Get(string projectRoot, string id) => null;

        public Result<Ticket> Add(string projectRoot, Ticket draft)
        {
            var ticket = draft with { Id = "0001-" + Fleet.Shared.Orchestrations.OrchestrationSlug.Of(draft.Title) };
            Added.Add((projectRoot, ticket));
            return Result<Ticket>.Ok(ticket);
        }

        public Result Save(string projectRoot, Ticket ticket) => Result.Ok();

        public Ticket? FindByOrchestration(string projectRoot, string orchestration) => null;
    }

    private sealed class FakeProjects(params Project[] projects) : IProjectStore
    {
        public Project? Load(string name) =>
            projects.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Project> List() => projects;

        public void Save(Project project)
        {
        }

        public void Remove(string name)
        {
        }
    }
}
