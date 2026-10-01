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
    private readonly string _directory = directory ?? FleetPaths.Status;

    public Task ReportAsync(AgentReport report, CancellationToken ct = default)
    {
        var file = Path.Combine(_directory, NameFor(report.Worktree, report.Session));

        try
        {
            if (report.State == AgentState.Unknown)
            {
                File.Delete(file);
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

            Directory.CreateDirectory(_directory);

            var temp = $"{file}.{Guid.NewGuid():N}.tmp";

            File.WriteAllText(temp, json);
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
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
                    Forget(file);
                    continue;
                }

                reports.Add(report);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return Task.FromResult(new AgentSnapshot(reports));
    }

    public static string NameFor(string worktree, string session)
    {
        var key = PathKey.For(worktree);
        var folded = OperatingSystem.IsWindows() ? key.ToLowerInvariant() : key;
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(folded)))[..16];
        var safe = new string([.. session.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')]);

        return $"{hash}-{(safe.Length == 0 ? "none" : safe)}.json";
    }

    private static AgentReport? Read(string file)
    {
        try
        {
            var stored = JsonSerializer.Deserialize(File.ReadAllText(file), FleetJsonContext.Default.AgentStateFile);

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
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void Forget(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
