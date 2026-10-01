using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;

namespace Fleet.Tests.Shared.Aidlc;

public class UnitGraphTests
{
    private static readonly string[] Repos = ["fleet-tui", "docs"];

    private static WorkUnit Unit(
        string id,
        string[]? dependsOn = null,
        string[]? acceptance = null,
        string[]? owns = null,
        string repository = "fleet-tui",
        string? branch = null,
        bool skeleton = false) =>
        new(
            id,
            $"unit {id}",
            repository,
            branch ?? $"aidlc/x/{id.ToLowerInvariant()}",
            dependsOn ?? [],
            acceptance ?? ["AC-1"],
            owns ?? [$"src/{id}/**"],
            "dotnet test",
            skeleton);

    [Fact]
    public void A_sound_plan_validates()
    {
        var graph = new UnitGraph(
        [
            Unit("U1", acceptance: ["AC-1"]),
            Unit("U2", dependsOn: ["U1"], acceptance: ["AC-2"]),
        ]);

        var result = graph.Validate(Repos, ["AC-1", "AC-2"]);

        Assert.True(result.Succeeded, result.Error);
    }

    [Fact]
    public void An_empty_plan_is_a_problem()
    {
        Assert.Contains("the plan has no units", new UnitGraph([]).Problems(Repos, []));
    }

    [Fact]
    public void A_cycle_is_found_and_named()
    {
        var graph = new UnitGraph(
        [
            Unit("U1", dependsOn: ["U3"]),
            Unit("U2", dependsOn: ["U1"]),
            Unit("U3", dependsOn: ["U2"]),
        ]);

        var cycle = graph.Cycle();

        Assert.Equal(4, cycle.Count);
        Assert.Equal(cycle[0], cycle[^1]);
        Assert.Contains(graph.Problems(Repos, ["AC-1"]), p => p.StartsWith("the dependencies form a cycle", StringComparison.Ordinal));
    }

    [Fact]
    public void A_unit_depending_on_itself_is_a_cycle()
    {
        Assert.NotEmpty(new UnitGraph([Unit("U1", dependsOn: ["U1"])]).Cycle());
    }

    [Fact]
    public void An_acyclic_graph_has_no_cycle()
    {
        var graph = new UnitGraph(
        [
            Unit("U1"),
            Unit("U2", dependsOn: ["U1"]),
            Unit("U3", dependsOn: ["U1"]),
            Unit("U4", dependsOn: ["U2", "U3"]),
        ]);

        Assert.Empty(graph.Cycle());
    }

    [Fact]
    public void Unknown_repositories_and_dependencies_are_problems()
    {
        var problems = new UnitGraph(
        [
            Unit("U1", repository: "nowhere"),
            Unit("U2", dependsOn: ["U9"]),
        ]).Problems(Repos, ["AC-1"]);

        Assert.Contains("unit U1 names an unknown repository 'nowhere'", problems);
        Assert.Contains("unit U2 depends on an unknown unit 'U9'", problems);
    }

    [Fact]
    public void Duplicate_branches_and_ids_are_problems()
    {
        var problems = new UnitGraph(
        [
            Unit("U1", branch: "aidlc/x/same"),
            Unit("U2", branch: "aidlc/x/same"),
            Unit("u2", branch: "aidlc/x/other"),
        ]).Problems(Repos, ["AC-1"]);

        Assert.Contains("branch aidlc/x/same is used by more than one unit", problems);
        Assert.Contains("unit id U2 is used more than once", problems);
    }

    [Fact]
    public void A_missing_id_or_branch_is_a_problem()
    {
        var problems = new UnitGraph([Unit(" ", branch: " ")]).Problems(Repos, ["AC-1"]);

        Assert.Contains("every unit needs an id", problems);
        Assert.Contains(problems, p => p.EndsWith("has no branch", StringComparison.Ordinal));
    }

    [Fact]
    public void Uncovered_and_unknown_acceptance_criteria_are_problems()
    {
        var problems = new UnitGraph([Unit("U1", acceptance: ["AC-1", "AC-7"])])
            .Problems(Repos, ["AC-1", "AC-2"]);

        Assert.Contains("AC-2 is not covered by any unit", problems);
        Assert.Contains("a unit claims AC-7, which the spec does not define", problems);
    }

    [Fact]
    public void The_ready_set_holds_units_whose_dependencies_are_all_settled()
    {
        var graph = new UnitGraph(
        [
            Unit("U1"),
            Unit("U2", dependsOn: ["U1"]),
            Unit("U3", dependsOn: ["U1", "U2"]),
            Unit("U4"),
        ]);

        Assert.Equal(["U1", "U4"], graph.Ready(new Dictionary<string, UnitState>()).Select(u => u.Id));

        var states = new Dictionary<string, UnitState>
        {
            ["U1"] = UnitState.Done,
            ["U4"] = UnitState.Building,
        };

        Assert.Equal(["U2"], graph.Ready(states).Select(u => u.Id));

        states["U2"] = UnitState.Skipped;

        Assert.Equal(["U3"], graph.Ready(states).Select(u => u.Id));
    }

    [Fact]
    public void A_unit_with_an_unknown_dependency_is_never_ready()
    {
        var graph = new UnitGraph([Unit("U1", dependsOn: ["ghost"])]);

        Assert.Empty(graph.Ready(new Dictionary<string, UnitState>()));
    }

    [Fact]
    public void The_walking_skeleton_runs_alone_before_anything_else_is_ready()
    {
        var graph = new UnitGraph(
        [
            Unit("U1", skeleton: true),
            Unit("U2"),
        ]);

        Assert.Equal(["U1"], graph.Ready(new Dictionary<string, UnitState>()).Select(u => u.Id));
        Assert.Equal(
            ["U2"],
            graph.Ready(new Dictionary<string, UnitState> { ["U1"] = UnitState.Done }).Select(u => u.Id));
    }

    [Fact]
    public void Units_on_one_dependency_chain_are_not_concurrent()
    {
        var u1 = Unit("U1");
        var u2 = Unit("U2", dependsOn: ["U1"]);
        var u3 = Unit("U3", dependsOn: ["U2"]);
        var u4 = Unit("U4");
        var graph = new UnitGraph([u1, u2, u3, u4]);

        Assert.False(graph.Concurrent(u1, u3));
        Assert.False(graph.Concurrent(u3, u1));
        Assert.True(graph.Concurrent(u3, u4));
        Assert.False(graph.Concurrent(u1, u1));
    }

    [Fact]
    public void Overlapping_owns_between_concurrent_units_in_one_repository_are_reported()
    {
        var graph = new UnitGraph(
        [
            Unit("U1", owns: ["src/Fleet/Features/Hooks/**"]),
            Unit("U2", owns: ["src/Fleet/Features/Hooks/HookView.cs"]),
        ]);

        var overlap = Assert.Single(graph.Overlaps());

        Assert.Equal(("U1", "U2"), (overlap.First, overlap.Second));
        Assert.Equal("src/Fleet/Features/Hooks/**", overlap.FirstGlob);
    }

    [Fact]
    public void Overlapping_owns_are_fine_when_one_unit_waits_for_the_other()
    {
        var graph = new UnitGraph(
        [
            Unit("U1", owns: ["src/**"]),
            Unit("U2", dependsOn: ["U1"], owns: ["src/a.cs"]),
        ]);

        Assert.Empty(graph.Overlaps());
    }

    [Fact]
    public void The_same_paths_in_different_repositories_do_not_overlap()
    {
        var graph = new UnitGraph(
        [
            Unit("U1", owns: ["src/**"]),
            Unit("U2", owns: ["src/**"], repository: "docs"),
        ]);

        Assert.Empty(graph.Overlaps());
    }
}
