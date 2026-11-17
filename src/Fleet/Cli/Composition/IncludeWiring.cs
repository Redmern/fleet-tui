using System.Diagnostics;
using Fleet.Features.Diagnostics.RunDoctor.Models;
using Fleet.Features.Setup.WriteInclude;
using Fleet.Features.Setup.WriteInclude.Enums;
using Fleet.Platform.Claude;
using Fleet.Platform.Codex;
using Fleet.Platform.Pi;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Results;

namespace Fleet.Cli.Composition;

public static class IncludeWiring
{
    public const string IncludeOnlyFlag = "--include-only";

    private static readonly TimeSpan NewBinaryLimit = TimeSpan.FromSeconds(30);

    private static readonly string[] OpencodeConfigFiles = ["opencode.json", "opencode.jsonc", "config.json"];

    public static string IncludePath => OrchestratorInclude.PathIn(FleetPaths.Config);

    public static Result<IncludeWrite> Write() => IncludeWriter.Write(FleetPaths.Config);

    public static string? WriteHere() => Write() is { Succeeded: false } failed ? failed.Error : null;

    public static IncludeFacts Facts(IReadOnlyCollection<string> harnessesInUse) =>
        Facts(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable, harnessesInUse);

    public static IncludeFacts Facts(string home, Func<string, string?> env, IReadOnlyCollection<string> harnessesInUse)
    {
        var claudeHome = Set(env(ClaudeConfigHome.OverrideVariable)) ?? Path.Combine(home, ".claude");
        var opencodeHomes = new[]
            {
                Path.Combine(Set(env("XDG_CONFIG_HOME")) ?? Path.Combine(home, ".config"), "opencode"),
                Set(env("OPENCODE_CONFIG_DIR")),
            }
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

        return new IncludeFacts(
            home,
            IncludePath,
            Read(IncludePath),
            [Instruction(Path.Combine(home, "CLAUDE.md")), Instruction(Path.Combine(claudeHome, "CLAUDE.md"))],
            [.. opencodeHomes.Select(OpencodeConfig)],
            Path.Combine(Path.GetDirectoryName(CodexHarnessConfig.UserConfigIn(env("CODEX_HOME"), home))!, "AGENTS.md"),
            Path.Combine(Path.GetDirectoryName(PiHarnessConfig.TrustStoreIn(env("PI_CODING_AGENT_DIR"), home))!, "AGENTS.md"),
            harnessesInUse);
    }

    private static string? Set(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static InstructionFile Instruction(string path) => new(path, Read(path));

    private static InstructionFile OpencodeConfig(string folder) =>
        Instruction(OpencodeConfigFiles.Select(f => Path.Combine(folder, f)).FirstOrDefault(File.Exists)
            ?? Path.Combine(folder, OpencodeConfigFiles[0]));

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

    public static void OnDaemonStart(IFleetLog log)
    {
        var outcome = Write();

        if (!outcome.Succeeded)
        {
            log.Write($"fleetd: {outcome.Error}");
        }
        else if (outcome.Value == IncludeWrite.Written)
        {
            log.Write($"fleetd: wrote this build's orchestrator instructions to {IncludePath}");
        }
    }

    public static bool InstalledWrites(string executable) => InstalledWrites(executable, NewBinaryLimit);

    public static bool InstalledWrites(string executable, TimeSpan limit)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        start.ArgumentList.Add("setup");
        start.ArgumentList.Add(IncludeOnlyFlag);

        try
        {
            using var process = Process.Start(start);

            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var errors = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit(limit))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return Task.WaitAll([output, errors], limit) && process.ExitCode == 0;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return false;
        }
    }
}
