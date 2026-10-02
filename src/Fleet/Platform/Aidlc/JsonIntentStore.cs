using System.Text.Json;
using Fleet.Platform.Aidlc.Models;
using Fleet.Ports.Aidlc;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;
using Fleet.Shared.Orchestrations;

namespace Fleet.Platform.Aidlc;

public sealed class JsonIntentStore : IIntentStore
{
    private static readonly AidlcJsonContext Compact =
        new(new JsonSerializerOptions(AidlcJsonContext.Default.Options) { WriteIndented = false });

    public IntentState? Load(string folder)
    {
        var path = OrchestrationPaths.StateFile(folder);

        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var stored = JsonSerializer.Deserialize(File.ReadAllText(path), AidlcJsonContext.Default.IntentStateFile);

            return stored is null ? null : FromFile(stored);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(string folder, IntentState state)
    {
        var path = OrchestrationPaths.StateFile(folder);
        var temporary = path + ".tmp";

        Directory.CreateDirectory(folder);
        File.WriteAllText(temporary, JsonSerializer.Serialize(ToFile(state), AidlcJsonContext.Default.IntentStateFile));
        File.Move(temporary, path, overwrite: true);
    }

    public void Append(string folder, AuditEntry entry)
    {
        var line = new AuditLine
        {
            Timestamp = entry.Timestamp,
            Actor = Words.Of(entry.Actor),
            Event = entry.Event.ToString(),
            Unit = entry.Unit,
            Sha = entry.Sha,
            Detail = entry.Detail,
        };

        Directory.CreateDirectory(folder);
        File.AppendAllText(
            OrchestrationPaths.AuditFile(folder),
            JsonSerializer.Serialize(line, Compact.AuditLine) + "\n");
    }

    public IReadOnlyList<AuditEntry> Audit(string folder)
    {
        var path = OrchestrationPaths.AuditFile(folder);

        try
        {
            if (!File.Exists(path))
            {
                return [];
            }

            return File.ReadAllLines(path)
                .Where(l => l.Trim().Length > 0)
                .Select(Parse)
                .OfType<AuditEntry>()
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static AuditEntry? Parse(string text)
    {
        try
        {
            var line = JsonSerializer.Deserialize(text, AidlcJsonContext.Default.AuditLine);

            if (line is null
                || Words.Parse<AuditActor>(line.Actor) is not { } actor
                || Words.Parse<AuditEvent>(line.Event) is not { } kind)
            {
                return null;
            }

            return new AuditEntry(line.Timestamp, actor, kind, line.Unit, line.Sha, line.Detail);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IntentStateFile ToFile(IntentState state) => new()
    {
        Slug = state.Slug,
        Profile = Words.Of(state.Profile),
        Autonomy = Words.Of(state.Autonomy),
        Stages = [.. state.Stages.Select(s => new StageFile
        {
            Stage = Words.Of(s.Stage),
            State = Words.Of(s.State),
            HumanGate = s.HumanGate,
            Reason = s.Reason,
        })],
        Units = [.. state.Units.Select(u => new UnitFile
        {
            Id = u.Unit.Id,
            Title = u.Unit.Title,
            Repository = u.Unit.Repository,
            Branch = u.Unit.Branch,
            DependsOn = [.. u.Unit.DependsOn],
            Acceptance = [.. u.Unit.Acceptance],
            Owns = [.. u.Unit.Owns],
            Verify = u.Unit.Verify,
            Skeleton = u.Unit.Skeleton,
            State = Words.Of(u.State),
        })],
        Created = state.Created,
        Updated = state.Updated,
    };

    private static IntentState? FromFile(IntentStateFile stored)
    {
        if (Words.Parse<Profile>(stored.Profile) is not { } profile)
        {
            return null;
        }

        var stages = stored.Stages
            .Select(s => Words.Parse<Stage>(s.Stage) is { } stage && Words.Parse<StageState>(s.State) is { } state
                ? new StagePlan(stage, s.HumanGate, state, s.Reason)
                : null)
            .OfType<StagePlan>()
            .ToList();

        var units = stored.Units
            .Select(u => new UnitEntry(
                new WorkUnit(u.Id, u.Title, u.Repository, u.Branch, u.DependsOn, u.Acceptance, u.Owns, u.Verify, u.Skeleton),
                Words.Parse<UnitState>(u.State) ?? UnitState.Blocked))
            .ToList();

        return new IntentState(
            stored.Slug,
            profile,
            Words.Parse<Autonomy>(stored.Autonomy) ?? Autonomy.Guided,
            stages,
            units,
            stored.Created,
            stored.Updated);
    }
}
