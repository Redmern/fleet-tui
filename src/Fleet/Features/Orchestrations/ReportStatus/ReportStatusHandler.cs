using System.Globalization;
using Fleet.Features.Orchestrations.ReportStatus.Models;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Todos;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Mcp;
using Fleet.Shared.Messaging;
using Fleet.Shared.Results;
using Fleet.Shared.Status;

namespace Fleet.Features.Orchestrations.ReportStatus;

public sealed class ReportStatusHandler(
    IAgentStore store,
    Func<DateTimeOffset>? clock = null,
    IAgentInboxes? inboxes = null,
    ITicketStatusSink? tickets = null,
    Func<OwnerNotice, CancellationToken, Task<bool>>? typeToOwner = null,
    string mainHarness = AgentHarness.Claude)
{
    public const string SessionEndedSummary = "session ended without a report";

    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.UtcNow);

    public Result<string> Handle(
        string project, string caller, string status, string summary, string projectRoot = "") =>
        Record(project, projectRoot, caller, status, summary, out _);

    public async Task<Result<string>> HandleAsync(
        string project, string root, string caller, string status, string summary, CancellationToken ct = default)
    {
        var recorded = Record(project, root, caller, status, summary, out var reporter);

        if (!recorded.Succeeded
            || OrchestrationStatus.Normalize(status) is not (OrchestrationStatus.Done or OrchestrationStatus.Failed)
            || (IsTopLevelSub(reporter!) && HasNativeMessaging(reporter!)))
        {
            return recorded;
        }

        var line = recorded.Value!.ReplaceLineEndings(" ");
        var notice = BuildNotice(project, root, reporter!, line);

        if (HarnessRoute.Native(HarnessOf(reporter!), notice.Owner is null ? mainHarness : HarnessOf(notice.Owner)))
        {
            return inboxes is not null
                   && await inboxes.AddressAsync(notice.Folder, ct).ConfigureAwait(false) is { } address
                ? Result<string>.Ok($"{recorded.Value}\n\n{PeerMessage.TellOwner(notice.Name, address, line)}")
                : recorded;
        }

        if (typeToOwner is null)
        {
            return recorded;
        }

        var told = await typeToOwner(notice, ct).ConfigureAwait(false);

        return Result<string>.Ok($"{recorded.Value}\n\n{PeerMessage.OwnerTyped(notice.Name, told)}");
    }

    public async Task<bool> SessionEndedAsync(
        string project, string root, AgentRecord record, CancellationToken ct = default)
    {
        if (ClosingMark.Exists(record.Worktree))
        {
            return false;
        }

        var failedAt = _now().ToString("O", CultureInfo.InvariantCulture);
        AgentRecord? failed = null;

        var kept = record.Open && store.Update(project, record.Worktree, current =>
        {
            if (!current.Open || OrchestrationStatus.Normalize(current.Status) != OrchestrationStatus.Working)
            {
                return current;
            }

            failed = current;
            return current with
            {
                Status = OrchestrationStatus.Failed,
                Summary = SessionEndedSummary,
                ReportedAt = failedAt,
            };
        });

        if (!kept || failed is null)
        {
            return false;
        }

        var isAgent = !AgentHarness.IsOrchestrator(failed.Harness);

        if (!isAgent && root.Trim().Length > 0)
        {
            tickets?.Reported(root, failed.Branch, OrchestrationStatus.Failed, SessionEndedSummary);
        }

        if ((IsTopLevelSub(failed) && HasNativeMessaging(failed)) || typeToOwner is null)
        {
            return true;
        }

        var slug = isAgent ? $"{failed.Repository}/{failed.Branch}" : failed.Branch;
        var line = ReportNote.For(slug, OrchestrationStatus.Failed, SessionEndedSummary);

        await typeToOwner(BuildNotice(project, root, failed, line), ct).ConfigureAwait(false);

        return true;
    }

    public OwnerNotice? NoticeFor(string project, string root, AgentRecord reporter, string line)
    {
        if (IsTopLevelSub(reporter))
        {
            return null;
        }

        return BuildNotice(project, root, reporter, line);
    }

    private OwnerNotice BuildNotice(string project, string root, AgentRecord reporter, string line)
    {
        var (owner, name, folder) = OwnerOf(project, root, reporter);

        return new OwnerNotice(owner, name, folder, HarnessRoute.OwnerNotice(line));
    }

    private Result<string> Record(
        string project, string root, string caller, string status, string summary, out AgentRecord? record)
    {
        record = null;

        if (caller.Trim().Length == 0)
        {
            return Result<string>.Fail(
                "report is for sub-orchestrators and agents; you are the main orchestrator.");
        }

        var isAgent = caller.StartsWith(McpCaller.AgentPrefix, StringComparison.Ordinal);
        var id = isAgent ? caller[McpCaller.AgentPrefix.Length..] : caller;

        record = Find(project, caller);

        if (record is null)
        {
            return Result<string>.Fail(
                isAgent ? $"no agent '{id}' in {project}." : $"no sub-orchestrator named '{id}' in {project}.");
        }

        var normalized = OrchestrationStatus.Normalize(status);

        var reportedAt = _now().ToString("O", CultureInfo.InvariantCulture);
        var kept = store.Update(project, record.Worktree, current => current with
        {
            Status = normalized,
            Summary = summary.Trim(),
            ReportedAt = reportedAt,
        });

        if (!kept)
        {
            return Result<string>.Fail(
                $"could not record the report for '{id}': it was removed from {project}, "
                + "or fleet could not update its session file.");
        }

        if (!isAgent && root.Trim().Length > 0)
        {
            tickets?.Reported(root, record.Branch, normalized, summary.Trim());
        }

        return Result<string>.Ok(ReportNote.For(id, normalized, summary));
    }

    private AgentRecord? Find(string project, string caller)
    {
        var isAgent = caller.StartsWith(McpCaller.AgentPrefix, StringComparison.Ordinal);
        var id = isAgent ? caller[McpCaller.AgentPrefix.Length..] : caller;

        return store.List(project).FirstOrDefault(a => isAgent
            ? !AgentHarness.IsOrchestrator(a.Harness)
              && string.Equals($"{a.Repository}/{a.Branch}", id, StringComparison.OrdinalIgnoreCase)
            : AgentHarness.IsOrchestrator(a.Harness)
              && string.Equals(a.Branch, id, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTopLevelSub(AgentRecord reporter) =>
        AgentHarness.IsOrchestrator(reporter.Harness) && reporter.Owner.Trim().Length == 0;

    private static bool HasNativeMessaging(AgentRecord record) => !HarnessRoute.LacksNative(HarnessOf(record));

    private static string HarnessOf(AgentRecord record) => record.RunningHarness;

    private (AgentRecord? Owner, string Name, string Folder) OwnerOf(string project, string root, AgentRecord reporter)
    {
        var owner = reporter.Owner.Trim().Length == 0 ? null : Find(project, reporter.Owner.Trim());

        return owner switch
        {
            null => (null, SessionNames.MainOrchestrator(project), root),
            _ when AgentHarness.IsOrchestrator(owner.Harness) =>
                (owner, SessionNames.SubOrchestrator(project, owner.Branch), owner.Worktree),
            _ => (owner, SessionNames.RepoAgent(project, owner.Repository, owner.Branch), owner.Worktree),
        };
    }
}
