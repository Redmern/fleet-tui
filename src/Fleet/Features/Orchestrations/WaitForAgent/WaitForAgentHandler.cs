using System.Globalization;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;
using Fleet.Shared.Status;

namespace Fleet.Features.Orchestrations.WaitForAgent;

public sealed class WaitForAgentHandler(
    IAgentStore store,
    Func<AgentRecord, CancellationToken, Task<string?>> paneText,
    Func<DateTime>? clock = null,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    TimeSpan? poll = null,
    TimeSpan? quietAfter = null)
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(300);

    public static readonly TimeSpan MaxTimeout = TimeSpan.FromSeconds(3600);

    private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;

    private readonly TimeSpan _poll = poll ?? TimeSpan.FromSeconds(3);

    private readonly TimeSpan _quietAfter = quietAfter ?? PaneQuiet.QuietAfter;

    public static TimeSpan TimeoutFrom(int seconds) =>
        seconds <= 0 ? DefaultTimeout : TimeSpan.FromSeconds(Math.Min(seconds, MaxTimeout.TotalSeconds));

    public async Task<Result<string>> HandleAsync(
        string project, string repository, string branch, TimeSpan timeout, CancellationToken ct = default)
    {
        var id = repository.Trim().Length == 0 ? branch.Trim() : $"{repository.Trim()}/{branch.Trim()}";
        var deadline = _now() + timeout;

        while (true)
        {
            var agent = Find(project, repository, branch);

            if (agent is null)
            {
                return Result<string>.Fail($"No agent {id} in this project.");
            }

            if (Finished(agent))
            {
                return Result<string>.Ok(Reported(id, agent));
            }

            var text = await paneText(agent, ct).ConfigureAwait(false);

            if (text is null)
            {
                return Result<string>.Ok($"{id}: not open, so it is not working; open_agent or tell_agent it.");
            }

            var now = _now();

            if (PaneQuiet.Quiet(PaneQuiet.Observe(agent.Worktree, text, now), now, _quietAfter))
            {
                return Result<string>.Ok(
                    $"{id}: idle — no pane output for {Seconds(_quietAfter)} and no done report. "
                    + "It may be waiting on a question: ask it with tell_agent what it needs.");
            }

            if (now >= deadline)
            {
                return Result<string>.Ok(
                    $"{id}: still working after {Seconds(timeout)}; call wait_for_agent again to keep waiting.");
            }

            await _delay(Min(_poll, deadline - now), ct).ConfigureAwait(false);
        }
    }

    private AgentRecord? Find(string project, string repository, string branch) =>
        store.List(project).FirstOrDefault(a => repository.Trim().Length == 0
            ? AgentHarness.IsOrchestrator(a.Harness)
              && string.Equals(a.Branch, branch.Trim(), StringComparison.OrdinalIgnoreCase)
            : !AgentHarness.IsOrchestrator(a.Harness)
              && string.Equals(a.Repository, repository.Trim(), StringComparison.OrdinalIgnoreCase)
              && string.Equals(a.Branch, branch.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool Finished(AgentRecord agent) =>
        OrchestrationStatus.Normalize(agent.Status) is OrchestrationStatus.Done or OrchestrationStatus.Failed
        && !ReportedBeforeLastInstruction(agent);

    private static bool ReportedBeforeLastInstruction(AgentRecord agent)
    {
        var instruction = Path.Combine(agent.Worktree, ".fleet", AgentHarness.AgentInstructionFile);

        return DateTimeOffset.TryParse(
                   agent.ReportedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var reported)
               && File.Exists(instruction)
               && reported.UtcDateTime < File.GetLastWriteTimeUtc(instruction);
    }

    private static string Reported(string id, AgentRecord agent)
    {
        var status = OrchestrationStatus.Normalize(agent.Status);

        return agent.Summary.Trim().Length == 0 ? $"{id}: {status}" : $"{id}: {status} — {agent.Summary.Trim()}";
    }

    private static string Seconds(TimeSpan span) =>
        $"{((int)span.TotalSeconds).ToString(CultureInfo.InvariantCulture)}s";

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
