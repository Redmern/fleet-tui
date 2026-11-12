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

namespace Fleet.Features.Orchestrations.ReportStatus;

public sealed class ReportStatusHandler(
    IAgentStore store,
    Func<DateTimeOffset>? clock = null,
    IAgentInboxes? inboxes = null,
    ITicketStatusSink? tickets = null,
    Func<OwnerNotice, CancellationToken, Task<bool>>? typeToOwner = null,
    string mainHarness = AgentHarness.Claude)
{
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
            || IsTopLevelSub(reporter!))
        {
            return recorded;
        }

        var line = recorded.Value!.ReplaceLineEndings(" ");
        var notice = NoticeFor(project, root, reporter!, line)!;

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

    public OwnerNotice? NoticeFor(string project, string root, AgentRecord reporter, string line)
    {
        if (IsTopLevelSub(reporter))
        {
            return null;
        }

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

        var kept = store.Replace(project, record with
        {
            Status = normalized,
            Summary = summary.Trim(),
            ReportedAt = _now().ToString("O", CultureInfo.InvariantCulture),
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
