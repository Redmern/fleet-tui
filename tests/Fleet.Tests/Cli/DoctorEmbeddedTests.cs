using Fleet.Cli.Composition;
using Fleet.Features.Diagnostics.RunDoctor;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;

namespace Fleet.Tests.Cli;

[Collection(ConfigHomeCollection.Name)]
public sealed class DoctorEmbeddedTests : ConfigHomeFixture
{
    private const string Saved = """
        {"version":1,"workspaces":[
          {"name":"alpha","activeTab":0,"floatsShown":false,
           "tabs":[{"title":"","activePane":0,"zoomed":-1,"root":{"sideBySide":true,"ratio":0.5,
             "first":{"pane":{"cwd":"C:/a","args":["claude"],"env":{}}},
             "second":{"pane":{"cwd":"C:/a","args":["fleet","dash"],"env":{}}}}}],
           "floats":[{"pane":{"cwd":"C:/a","args":[],"env":{}},"x":1,"y":2,"width":40,"height":10,"title":""}]},
          {"name":"beta","activeTab":0,"floatsShown":false,
           "tabs":[{"title":"","activePane":0,"zoomed":-1,"root":{"pane":{"cwd":"C:/b","args":[],"env":{}}}}],
           "floats":[]}]}
        """;

    private static async Task<EmbeddedHealth> HealthWithoutFleetdAsync()
    {
        var before = Environment.GetEnvironmentVariable(Endpoint.Variable);
        Environment.SetEnvironmentVariable(Endpoint.Variable, $"fleet-doctor-{Guid.NewGuid():N}"[..24]);
        try
        {
            return await EmbeddedWiring.HealthAsync();
        }
        finally
        {
            Environment.SetEnvironmentVariable(Endpoint.Variable, before);
        }
    }

    [Fact]
    public async Task Reads_the_saved_session_and_counts_its_panes_when_fleetd_is_not_running()
    {
        var endpoint = $"fleet-doctor-{Guid.NewGuid():N}"[..24];
        var before = Environment.GetEnvironmentVariable(Endpoint.Variable);
        Environment.SetEnvironmentVariable(Endpoint.Variable, endpoint);
        try
        {
            await File.WriteAllTextAsync(EmbeddedWiring.SessionFile(endpoint), Saved);

            var health = await EmbeddedWiring.HealthAsync();

            Assert.Null(health.Fleetd);
            Assert.Null(health.SavedError);
            Assert.Equal((2, 4), (health.Saved!.Workspaces, health.Saved.Panes));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Endpoint.Variable, before);
        }
    }

    [Fact]
    public async Task No_saved_session_is_not_a_problem()
    {
        var health = await HealthWithoutFleetdAsync();

        Assert.Null(health.Saved);
        Assert.Null(health.SavedError);
    }

    [Fact]
    public async Task An_unreadable_saved_session_is_a_problem_the_doctor_reports()
    {
        var endpoint = $"fleet-doctor-{Guid.NewGuid():N}"[..24];
        var before = Environment.GetEnvironmentVariable(Endpoint.Variable);
        Environment.SetEnvironmentVariable(Endpoint.Variable, endpoint);
        try
        {
            await File.WriteAllTextAsync(EmbeddedWiring.SessionFile(endpoint), "{ not json");

            var health = await EmbeddedWiring.HealthAsync();
            var report = await new RunDoctorHandler(new FakeMuxDriver(), new NoProjects(), new NoLog(), () => Task.FromResult<string?>("git"))
                .HandleAsync(new RunDoctorCommand("embedded", null, health));

            Assert.NotNull(health.SavedError);
            Assert.Contains(report.Problems, p => p.Contains("saved embedded session", StringComparison.Ordinal));
            Assert.Same(health, report.Embedded);
        }
        finally
        {
            Environment.SetEnvironmentVariable(Endpoint.Variable, before);
        }
    }

    private sealed class NoLog : IFleetLog
    {
        public void Swallowed(Exception e)
        {
        }

        public void Write(string line)
        {
        }

        public IReadOnlyList<string> Tail(int lines) => [];
    }

    private sealed class NoProjects : IProjectStore
    {
        public Project? Load(string name) => null;

        public IReadOnlyList<Project> List() => [];

        public void Save(Project project)
        {
        }

        public void Remove(string name)
        {
        }
    }
}
