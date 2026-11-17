using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Aidlc;
using Fleet.Ports.Settings;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Aidlc.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Harness;
using Fleet.Shared.Mcp;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Orchestrations.SetProfile;

public sealed class SetProfileHandler(IAgentStore store, IIntentStore intents, ISettingsStore? settings = null)
{
    public const string OnlySubs = "set_profile: only a sub-orchestrator can set its orchestration's profile.";

    public Result<string> Handle(string project, string projectRoot, string caller, string profile, string stampUtc)
    {
        if (Sub(project, caller) is not { } sub)
        {
            return Result<string>.Fail(OnlySubs);
        }

        if (Words.Parse<Profile>(profile) is not { } wanted)
        {
            return Result<string>.Fail(
                $"set_profile: '{profile.Trim()}' is not a profile. Use one of: "
                + string.Join(", ", ProfileCatalog.All.Select(p => Words.Of(p)))
                + ".");
        }

        var folder = sub.Worktree;

        if (intents.Load(folder) is not { } state)
        {
            return Result<string>.Fail(
                $"set_profile: {OrchestrationPaths.StateFile(folder)} is missing or unreadable, so this "
                + "orchestration does not run under Ai-DLC and has no profile to change.");
        }

        if (state.Profile == wanted)
        {
            return Result<string>.Ok($"this orchestration already runs the {Words.Of(wanted)} profile; nothing changed.");
        }

        var off = (settings?.Load(project) ?? SettingsConfig.Default).Aidlc.Off;
        var stages = Regenerated(state.Stages, ProcessPlan.Resolve(wanted, state.Autonomy, off));
        var changed = state with { Profile = wanted, Stages = stages, Updated = stampUtc };

        try
        {
            intents.Save(folder, changed);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Result<string>.Fail(
                $"set_profile: could not write {OrchestrationPaths.StateFile(folder)} ({e.Message}); nothing changed.");
        }

        intents.Append(
            folder,
            new AuditEntry(
                stampUtc,
                AuditActor.Conductor,
                AuditEvent.ProfileSet,
                Detail: $"{Words.Of(wanted)} (was {Words.Of(state.Profile)})"));

        var instructions = OrchestrationPaths.InstructionsFile(folder, sub.OrchestratorProfile);
        var rewritten = Rewrite(
            instructions,
            text => OrchestrationText.WithResearch(
                OrchestrationText.WithProcess(
                    text,
                    ProcessText.Render(new EffectivePlan(wanted, state.Autonomy, stages), ProcessGuidance(projectRoot))),
                wanted == Profile.Research || (OrchestrationText.HasResearch(text) && state.Profile != Profile.Research),
                sub.OrchestratorProfile.Has(HarnessCapabilities.NativeMessaging)));

        return Result<string>.Ok(
            $"profile set to {Words.Of(wanted)} (was {Words.Of(state.Profile)}): stages that state.json shows as "
            + "started are kept and the rest follow the new profile. fleet does not track your progress through "
            + "the stages yet, so carry on from the stage you are in, and keep the artifacts and approvals of the "
            + "stages you finished. "
            + (rewritten
                ? $"Read the Process section of {Path.GetFileName(instructions)} again: it has the new stage text."
                : $"{Path.GetFileName(instructions)} could not be rewritten; state.json has the new stages."));
    }

    private AgentRecord? Sub(string project, string caller)
    {
        var id = caller.Trim();

        if (id.Length == 0 || id.StartsWith(McpCaller.AgentPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        return store.List(project).FirstOrDefault(a =>
            AgentHarness.IsOrchestrator(a.Harness) && string.Equals(a.Branch, id, StringComparison.OrdinalIgnoreCase));
    }

    private static List<StagePlan> Regenerated(IReadOnlyList<StagePlan> current, EffectivePlan plan)
    {
        var started = current.Where(Started).ToList();
        var fresh = plan.Stages.Where(s => started.All(k => k.Stage != s.Stage));

        return [.. started.Concat(fresh).OrderBy(s => s.Stage)];
    }

    private static bool Started(StagePlan stage) =>
        stage.State switch
        {
            StageState.Pending => false,
            StageState.Skipped => stage.Reason != ProcessPlan.OffInSettings,
            _ => true,
        };

    private static bool Rewrite(string path, Func<string, string> rewrite)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            File.WriteAllText(path, rewrite(File.ReadAllText(path)));

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ProcessGuidance(string projectRoot)
    {
        var path = ProjectConfigPaths.ProcessFile(projectRoot);

        try
        {
            var text = File.Exists(path) ? File.ReadAllText(path) : null;

            return text is not null && OrchestrationText.IsClassicAidlc(text) ? null : text;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
