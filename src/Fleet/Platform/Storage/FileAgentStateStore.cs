using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Agents;
using Fleet.Shared;
using Fleet.Shared.Status;
using Fleet.Shared.Status.Enums;
using Fleet.Shared.Status.Models;

namespace Fleet.Platform.Storage;

public sealed class FileAgentStateStore(string? directory = null) : IAgentStateStore
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan StaleTemp = TimeSpan.FromMinutes(1);

    private readonly string _directory = directory ?? FleetPaths.Status;

    public Task ReportAsync(AgentReport report, CancellationToken ct = default)
    {
        var file = Path.Combine(_directory, NameFor(report.Worktree, report.Session));

        if (report.State == AgentState.Unknown)
        {
            Delete(file);
            return Task.CompletedTask;
        }

        var json = JsonSerializer.Serialize(
            new AgentStateFile
            {
                Worktree = report.Worktree,
                Session = report.Session,
                State = report.State.ToString(),
                At = report.At,
                Transcript = report.Transcript,
                Reason = report.Reason,
            },
            FleetJsonContext.Default.AgentStateFile);

        var temp = $"{file}.{Guid.NewGuid():N}.tmp";

        var written = BusyFiles.Retry(() =>
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temp, json);
            File.Move(temp, file, overwrite: true);
            return file;
        }, Patience);

        if (written is null)
        {
            Delete(temp);
        }
        else if (report.StartsSession)
        {
            ForgetOtherSessions(report.Worktree, file);
        }

        return Task.CompletedTask;
    }

    public Task<AgentSnapshot> GetSnapshotAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_directory))
        {
            return Task.FromResult(AgentSnapshot.Empty);
        }

        var now = DateTime.UtcNow;
        var reports = new List<AgentReport>();

        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                if (Read(file) is not { } report)
                {
                    continue;
                }

                if (AgentStatusRules.Forgotten(report, now))
                {
                    Delete(file);
                    continue;
                }

                reports.Add(report);
            }

            foreach (var temp in Directory.EnumerateFiles(_directory, "*.tmp"))
            {
                if (now - File.GetLastWriteTimeUtc(temp) >= StaleTemp)
                {
                    Delete(temp);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return Task.FromResult(new AgentSnapshot(reports));
    }

    public static string NameFor(string worktree, string session)
    {
        var safe = new string([.. session.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')]);

        return $"{PrefixFor(worktree)}{(safe.Length == 0 ? "none" : safe)}.json";
    }

    private static string PrefixFor(string worktree)
    {
        var key = PathKey.For(worktree);
        var folded = OperatingSystem.IsWindows() ? key.ToLowerInvariant() : key;

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folded)))[..16] + "-";
    }

    private void ForgetOtherSessions(string worktree, string keep)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(_directory, PrefixFor(worktree) + "*.json"))
            {
                if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase))
                {
                    Delete(file);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AgentReport? Read(string file)
    {
        var text = BusyFiles.Retry(() =>
        {
            using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }, TimeSpan.FromMilliseconds(200));

        if (text is null)
        {
            return null;
        }

        try
        {
            var stored = JsonSerializer.Deserialize(text, FleetJsonContext.Default.AgentStateFile);

            if (stored is null
                || stored.Worktree.Length == 0
                || !Enum.TryParse<AgentState>(stored.State, out var state))
            {
                return null;
            }

            return new AgentReport(
                stored.Worktree,
                stored.Session,
                state,
                DateTime.SpecifyKind(stored.At, DateTimeKind.Utc),
                stored.Transcript,
                stored.Reason);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void Delete(string file) =>
        BusyFiles.Retry(() =>
        {
            File.Delete(file);
            return file;
        }, Patience);
}
