using Fleet.Platform.Aidlc;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Platform.Aidlc;

public sealed class JsonIntentStoreTests : IDisposable
{
    private readonly string _folder =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly JsonIntentStore _store = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static IntentState Sample()
    {
        var plan = ProcessPlan.Resolve(Profile.Feature, Autonomy.Automatic, AidlcPart.Learn);
        var state = Intake.Start("add-oauth", plan, ProfileSource.Prefix, "2026-10-02T10:00:00Z").State;

        var unit = new WorkUnit(
            "U1", "Hook reporter", "fleet-tui", "aidlc/add-oauth/u1", ["U0"], ["AC-1", "AC-3"],
            ["src/Fleet/Features/Hooks/**"], "dotnet test", Skeleton: true);

        return state with { Units = [new UnitEntry(unit, UnitState.Building)], Updated = "2026-10-02T11:00:00Z" };
    }

    [Fact]
    public void A_folder_without_a_record_loads_nothing()
    {
        Assert.Null(_store.Load(_folder));
        Assert.Empty(_store.Audit(_folder));
    }

    [Fact]
    public void The_state_round_trips()
    {
        var saved = Sample();

        _store.Save(_folder, saved);

        var loaded = _store.Load(_folder)!;

        Assert.Equal(saved.Slug, loaded.Slug);
        Assert.Equal(saved.Profile, loaded.Profile);
        Assert.Equal(saved.Autonomy, loaded.Autonomy);
        Assert.Equal(saved.Stages, loaded.Stages);
        Assert.Equal(saved.Created, loaded.Created);
        Assert.Equal(saved.Updated, loaded.Updated);

        var unit = Assert.Single(loaded.Units);

        Assert.Equal(UnitState.Building, unit.State);
        Assert.Equal("U1", unit.Unit.Id);
        Assert.Equal(["U0"], unit.Unit.DependsOn);
        Assert.Equal(["AC-1", "AC-3"], unit.Unit.Acceptance);
        Assert.Equal(["src/Fleet/Features/Hooks/**"], unit.Unit.Owns);
        Assert.True(unit.Unit.Skeleton);
    }

    [Fact]
    public void The_state_file_is_readable_json_with_words_not_numbers()
    {
        _store.Save(_folder, Sample());

        var raw = File.ReadAllText(OrchestrationPaths.StateFile(_folder));

        Assert.Contains("\"profile\": \"feature\"", raw);
        Assert.Contains("\"autonomy\": \"automatic\"", raw);
        Assert.Contains("\"stage\": \"intake\"", raw);
        Assert.Contains("\"state\": \"done\"", raw);
        Assert.Contains("\"reason\": \"off in settings\"", raw);
        Assert.False(File.Exists(OrchestrationPaths.StateFile(_folder) + ".tmp"));
    }

    [Fact]
    public void Saving_again_replaces_the_state()
    {
        _store.Save(_folder, Sample());
        _store.Save(_folder, Sample() with { Profile = Profile.Bugfix });

        Assert.Equal(Profile.Bugfix, _store.Load(_folder)!.Profile);
    }

    [Fact]
    public void A_corrupt_or_unknown_state_loads_nothing()
    {
        Directory.CreateDirectory(_folder);

        File.WriteAllText(OrchestrationPaths.StateFile(_folder), "{ not json");
        Assert.Null(_store.Load(_folder));

        File.WriteAllText(OrchestrationPaths.StateFile(_folder), """{"profile":"epic"}""");
        Assert.Null(_store.Load(_folder));
    }

    [Fact]
    public void The_audit_appends_one_json_line_per_event_in_order()
    {
        _store.Append(_folder, new AuditEntry("t1", AuditActor.Engine, AuditEvent.IntentCreated, Detail: "add-oauth"));
        _store.Append(_folder, new AuditEntry("t2", AuditActor.Human, AuditEvent.ProfileSet, "U1", "abc123", "feature"));

        var lines = File.ReadAllLines(OrchestrationPaths.AuditFile(_folder));

        Assert.Equal(2, lines.Length);
        Assert.All(lines, l => Assert.StartsWith("{", l));
        Assert.Contains("\"event\":\"IntentCreated\"", lines[0]);
        Assert.Contains("\"actor\":\"engine\"", lines[0]);

        var audit = _store.Audit(_folder);

        Assert.Equal(
            [
                new AuditEntry("t1", AuditActor.Engine, AuditEvent.IntentCreated, Detail: "add-oauth"),
                new AuditEntry("t2", AuditActor.Human, AuditEvent.ProfileSet, "U1", "abc123", "feature"),
            ],
            audit);
    }

    [Fact]
    public void A_damaged_audit_line_is_skipped_not_fatal()
    {
        _store.Append(_folder, new AuditEntry("t1", AuditActor.Engine, AuditEvent.IntentCreated));
        File.AppendAllText(OrchestrationPaths.AuditFile(_folder), "{ broken\n{\"event\":\"Invented\",\"actor\":\"engine\"}\n");
        _store.Append(_folder, new AuditEntry("t2", AuditActor.Engine, AuditEvent.StageSkipped));

        Assert.Equal(["t1", "t2"], _store.Audit(_folder).Select(e => e.Timestamp));
    }
}
