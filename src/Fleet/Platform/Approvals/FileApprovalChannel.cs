using System.Text.Json;
using Fleet.Platform.Approvals.Models;
using Fleet.Platform.Storage;
using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Enums;
using Fleet.Ports.Approvals.Models;
using Fleet.Shared;

namespace Fleet.Platform.Approvals;

public sealed class FileApprovalChannel(
    TimeSpan? poll = null,
    TimeSpan? timeout = null,
    TimeSpan? stale = null,
    Func<DateTimeOffset>? now = null) : IApprovalChannel, IApprovalInbox
{
    private readonly TimeSpan _poll = poll ?? TimeSpan.FromMilliseconds(100);

    private readonly TimeSpan _timeout = timeout ?? ApprovalRequest.DefaultWait;

    private readonly TimeSpan _stale = stale ?? TimeSpan.FromSeconds(12);

    private readonly TimeSpan _paneGrace = TimeSpan.FromSeconds(5);

    private readonly Func<DateTimeOffset> _clock = now ?? (() => DateTimeOffset.UtcNow);

    public async Task<ApprovalOutcome> AskAsync(
        ApprovalRequest request, CancellationToken ct = default)
    {
        var dir = DirFor(request.Project);

        if (dir is null)
        {
            return ApprovalOutcome.NoDashboard("This project has no name fleet can use.");
        }

        Directory.CreateDirectory(dir);

        if (!Alive(request.Project))
        {
            return ApprovalOutcome.NoDashboard(
                "No fleet dashboard is running, so this action cannot be approved.");
        }

        var stem = $"{_clock().UtcTicks:D19}-{Guid.NewGuid():n}";

        if (!TryWrite(Path.Combine(dir, stem + ".ask"), Serialize(request)))
        {
            return ApprovalOutcome.NoDashboard("fleet could not record the approval request.");
        }

        var wait = request.Timeout ?? _timeout;
        var deadline = (request.Arrived ?? _clock()) + wait;

        try
        {
            while (_clock() < deadline)
            {
                ct.ThrowIfCancellationRequested();

                var reply = Read(Path.Combine(dir, stem + ".reply"));

                if (reply is not null)
                {
                    return Decide(reply);
                }

                if (!File.Exists(Path.Combine(dir, stem + ".taken")) && !Alive(request.Project))
                {
                    return ApprovalOutcome.NoDashboard(
                        "The fleet dashboard closed before it answered.");
                }

                await Task.Delay(_poll, ct).ConfigureAwait(false);
            }

            return ApprovalOutcome.NoAnswer(request, wait);
        }
        finally
        {
            Cleanup(dir, stem);
        }
    }

    public void Heartbeat(string project)
    {
        var alive = AlivePath(project);

        if (alive is not null)
        {
            Directory.CreateDirectory(FleetPaths.Approvals);
            TryWriteText(alive, _clock().UtcTicks.ToString());
        }
    }

    public void Retire(string project)
    {
        var alive = AlivePath(project);

        if (alive is not null)
        {
            Discard(alive);
        }
    }

    public IReadOnlyList<PendingApproval> Pending(string project)
    {
        var dir = DirFor(project);

        if (dir is null || !Directory.Exists(dir))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(dir, "*.ask").Concat(Directory.EnumerateFiles(dir, "*.taken"))
            .GroupBy(f => Path.GetFileNameWithoutExtension(f) ?? string.Empty)
            .Where(g => !File.Exists(Path.Combine(dir, g.Key + ".reply")))
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        var pending = new List<PendingApproval>();

        foreach (var group in files)
        {
            var path = group.OrderByDescending(f => f.EndsWith(".taken", StringComparison.Ordinal)).First();
            var file = ReadAsk(path);

            if (AskedAt(group.Key) is { } asked
                && file.Tool.Length > 0
                && _clock() - asked < (file.TimeoutSeconds > 0 ? TimeSpan.FromSeconds(file.TimeoutSeconds) : _timeout))
            {
                pending.Add(new PendingApproval(
                    group.Key,
                    new ApprovalRequest(project, file.Tool, file.Summary, file.Pane, file.Caller),
                    asked));
            }
        }

        return pending;
    }

    public PendingApproval? TakePending(string project, string? pane = null)
    {
        var dir = DirFor(project);

        if (dir is null || !Directory.Exists(dir))
        {
            return null;
        }

        foreach (var ask in Directory.EnumerateFiles(dir, "*.ask").OrderBy(f => f, StringComparer.Ordinal))
        {
            var stem = Path.GetFileNameWithoutExtension(ask);

            if (!ForTaker(ReadAsk(ask).Pane, stem, pane))
            {
                continue;
            }

            var taken = Path.Combine(dir, stem + ".taken");

            try
            {
                File.Move(ask, taken, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var file = ReadAsk(taken);

            return new PendingApproval(stem, new ApprovalRequest(project, file.Tool, file.Summary, file.Pane, file.Caller));
        }

        return null;
    }

    public void Answer(string project, string id, ApprovalDecision decision)
    {
        var dir = DirFor(project);

        if (dir is not null && (File.Exists(Path.Combine(dir, id + ".taken")) || File.Exists(Path.Combine(dir, id + ".ask"))))
        {
            TryWrite(Path.Combine(dir, id + ".reply"), decision.ToString());
        }
    }

    private bool Alive(string project)
    {
        var alive = AlivePath(project);

        if (alive is null || !File.Exists(alive))
        {
            return false;
        }

        try
        {
            return long.TryParse(File.ReadAllText(alive), out var ticks)
                && _clock() - new DateTimeOffset(ticks, TimeSpan.Zero) < _stale;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return false;
        }
    }

    private static DateTimeOffset? AskedAt(string stem) =>
        long.TryParse(stem.Split('-')[0], out var ticks) ? new DateTimeOffset(ticks, TimeSpan.Zero) : null;

    private bool ForTaker(string? asker, string stem, string? taker)
    {
        if (taker is not null)
        {
            return asker == taker;
        }

        return asker is null
            || !long.TryParse(stem.Split('-')[0], out var ticks)
            || _clock() - new DateTimeOffset(ticks, TimeSpan.Zero) >= _paneGrace;
    }

    private static ApprovalOutcome Decide(string reply) =>
        Enum.TryParse<ApprovalDecision>(reply.Trim(), out var decision)
        && decision == ApprovalDecision.Allowed
            ? ApprovalOutcome.Allow
            : ApprovalOutcome.Deny("Declined by the user in fleet.");

    private string Serialize(ApprovalRequest request) =>
        JsonSerializer.Serialize(
            new AskFile { Tool = request.Tool, Summary = request.Summary, Pane = request.Pane, Caller = request.Caller, TimeoutSeconds = (int)(request.Timeout ?? _timeout).TotalSeconds },
            FleetJsonContext.Default.AskFile);

    private static AskFile ReadAsk(string path)
    {
        try
        {
            return JsonSerializer.Deserialize(File.ReadAllText(path), FleetJsonContext.Default.AskFile)
                ?? new AskFile();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new AskFile();
        }
    }

    private static string? Read(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Cleanup(string dir, string stem)
    {
        foreach (var extension in new[] { ".ask", ".taken", ".reply" })
        {
            Discard(Path.Combine(dir, stem + extension));
        }
    }

    private static bool TryWrite(string path, string content)
    {
        var temp = path + ".tmp";

        try
        {
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);

            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Discard(temp);

            return false;
        }
    }

    private static void TryWriteText(string path, string content)
    {
        try
        {
            File.WriteAllText(path, content);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Discard(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? DirFor(string project)
    {
        var name = ProjectName.Sanitize(project);

        return name.Length == 0 ? null : Path.Combine(FleetPaths.Approvals, name);
    }

    private static string? AlivePath(string project)
    {
        var name = ProjectName.Sanitize(project);

        return name.Length == 0 ? null : Path.Combine(FleetPaths.Approvals, name + ".alive");
    }
}
