using System.Globalization;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Mcp;
using Fleet.Shared.Messaging;
using Fleet.Shared.Results;

namespace Fleet.Features.Orchestrations.ReportStatus;

public sealed class ReportStatusHandler(
    IAgentStore store, Func<DateTimeOffset>? clock = null, IAgentInboxes? inboxes = null)
{
    private readonly Func<DateTimeOffset> _now = clock ?? (() => DateTimeOffset.UtcNow);

    public Result<string> Handle(string project, string caller, string status, string summary) =>
        Record(project, caller, status, summary, out _);

    public async Task<Result<string>> HandleAsync(
        string project, string root, string caller, string status, string summary, CancellationToken ct = default)
    {
        var recorded = Record(project, caller, status, summary, out var reporter);

        if (!recorded.Succeeded
            || inboxes is null
            || OrchestrationStatus.Normalize(status) is not (OrchestrationStatus.Done or OrchestrationStatus.Failed)
            || IsTopLevelSub(reporter!))
        {
            return recorded;
        }

        var (name, folder) = OwnerOf(project, root, reporter!);

        return await inboxes.AddressAsync(folder, ct).ConfigureAwait(false) is { } address
            ? Result<string>.Ok(
                $"{recorded.Value}\n\n{PeerMessage.TellOwner(name, address, recorded.Value!.ReplaceLineEndings(" "))}")
            : recorded;
    }

    private Result<string> Record(
        string project, string caller, string status, string summary, out AgentRecord? record)
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

        store.Save(project, record with
        {
            Status = normalized,
            Summary = summary.Trim(),
            ReportedAt = _now().ToString("O", CultureInfo.InvariantCulture),
        });

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

    private (string Name, string Folder) OwnerOf(string project, string root, AgentRecord reporter)
    {
        var owner = reporter.Owner.Trim().Length == 0 ? null : Find(project, reporter.Owner.Trim());

        return owner switch
        {
            null => (SessionNames.MainOrchestrator(project), root),
            _ when AgentHarness.IsOrchestrator(owner.Harness) =>
                (SessionNames.SubOrchestrator(project, owner.Branch), owner.Worktree),
            _ => (SessionNames.RepoAgent(project, owner.Repository, owner.Branch), owner.Worktree),
        };
    }
}
