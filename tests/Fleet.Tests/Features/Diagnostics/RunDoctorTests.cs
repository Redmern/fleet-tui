using Fleet.Features.Diagnostics.RunDoctor;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports;
using Fleet.Ports.Projects;
using Fleet.Ports.Projects.Models;

namespace Fleet.Tests.Features.Diagnostics;

public class RunDoctorTests
{
    [Fact]
    public async Task Reports_healthy_when_the_mux_answers_and_git_is_present()
    {
        var report = await Handler().HandleAsync(new RunDoctorCommand("fake", null));

        Assert.True(report.Healthy);
        Assert.Empty(report.Problems);
        Assert.True(report.MuxReachable);
    }

    [Fact]
    public async Task Reports_a_problem_when_the_mux_is_unreachable()
    {
        var report = await Handler(muxAvailable: false)
            .HandleAsync(new RunDoctorCommand("fake", null));

        Assert.False(report.Healthy);
        Assert.Contains(report.Problems, p => p.Contains("multiplexer"));
    }

    [Fact]
    public async Task Reports_a_problem_when_the_driver_is_not_implemented()
    {
        var report = await Handler().HandleAsync(
            new RunDoctorCommand("tmux", "the 'tmux' driver is not implemented yet"));

        Assert.False(report.Healthy);
        Assert.Contains(report.Problems, p => p.Contains("tmux"));
    }

    [Fact]
    public async Task Reports_a_problem_when_git_is_missing()
    {
        var report = await Handler(git: null).HandleAsync(new RunDoctorCommand("fake", null));

        Assert.False(report.Healthy);
        Assert.Contains(report.Problems, p => p.Contains("git"));
    }

    [Fact]
    public async Task Every_problem_is_collected_rather_than_only_the_first()
    {
        var report = await Handler(muxAvailable: false, git: null)
            .HandleAsync(new RunDoctorCommand("tmux", "unsupported driver"));

        Assert.Equal(3, report.Problems.Count);
    }

    private static RunDoctorHandler Handler(
        bool muxAvailable = true, string? git = "git version 2.52.0") =>
        new(new FakeMuxDriver { Available = muxAvailable },
            new EmptyStore(),
            new NullLog(),
            () => Task.FromResult(git));

    private sealed class NullLog : IFleetLog
    {
        public void Swallowed(Exception e)
        {
        }

        public void Write(string line)
        {
        }

        public IReadOnlyList<string> Tail(int lines) => [];
    }

    private sealed class EmptyStore : IProjectStore
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
