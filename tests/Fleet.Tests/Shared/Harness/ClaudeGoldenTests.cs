using System.Security.Cryptography;
using System.Text;
using Fleet.Shared.Constants;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Shared.Harness;

public sealed class ClaudeGoldenTests
{
    private static readonly ClaudeLaunch Launch = new("p-repo-feat-x", new RoleModel("opus", "high"));

    private static string Digest(IReadOnlyList<string> command, IReadOnlyDictionary<string, string> env) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("\u0001", command) + "\u0002"
            + string.Join("\u0001", env.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key}={e.Value}")))));

    public static TheoryData<string, string> Cases => new()
    {
        { "claude", "eb4c5efb8ca2fd22b7ffc121af31bd4f0fd19aa2e42947743f39c08e80f4d6e5" },
        { "claude-launch", "9e2809841c1b312e212a402a5de4cbab4bce58b928bf26d5b876c0f3ba0fbdab" },
        { "nvim", "15e0a263b22b4d7137cafbe88e81ebfc67a23b6c3b858247aeb2b174a780551f" },
        { "nvim-with-claude", "5bf06bc49d3947dc2a2156911c3a9e6ae067692b4647f690f2868d39fb6bf5de" },
        { "orchestrator-nvim", "720b72ba5f0bb65c7f8520cab6a00bd07bc0b873c707cb0f31743d83c95c5aa9" },
        { "orchestrator-bare", "9e2809841c1b312e212a402a5de4cbab4bce58b928bf26d5b876c0f3ba0fbdab" },
        { "orchestrator-resume-nvim", "039f716505c34f1a1b0f740c2bdcc36ee10322aec068f0b0abee941739f1ac61" },
        { "orchestrator-resume-bare", "bce80fece7de036d33a98e3ec1ddca5ec861ed99d2affe13b8aebb7b0f512185" },
        { "resumed-claude", "bce80fece7de036d33a98e3ec1ddca5ec861ed99d2affe13b8aebb7b0f512185" },
        { "resumed-nvim-with-claude", "b3d6c9409941a40b137b21005872693b0031882e9c99970dc918b3fc625d0160" },
        { "resumed-plain-nvim", "15e0a263b22b4d7137cafbe88e81ebfc67a23b6c3b858247aeb2b174a780551f" },
    };

    private static string Actual(string name) => name switch
    {
        "claude" => Digest(AgentHarness.CommandFor(AgentHarness.Claude), AgentHarness.SpawnEnv(AgentHarness.Claude)),
        "claude-launch" => Digest(
            AgentHarness.CommandFor(AgentHarness.Claude, launch: Launch), AgentHarness.SpawnEnv(AgentHarness.Claude)),
        "nvim" => Digest(AgentHarness.CommandFor(AgentHarness.Nvim), AgentHarness.SpawnEnv(AgentHarness.Nvim)),
        "nvim-with-claude" => Digest(
            AgentHarness.CommandFor(AgentHarness.Nvim, withClaude: true, launch: Launch),
            AgentHarness.SpawnEnv(AgentHarness.Nvim)),
        "orchestrator-nvim" => Digest(
            AgentHarness.CommandFor(AgentHarness.Orchestrator, launch: Launch),
            AgentHarness.SpawnEnv(AgentHarness.Orchestrator)),
        "orchestrator-bare" => Digest(
            AgentHarness.CommandFor(AgentHarness.Orchestrator, orchestratorInNvim: false, launch: Launch),
            AgentHarness.SpawnEnv(AgentHarness.Orchestrator, orchestratorInNvim: false)),
        "orchestrator-resume-nvim" => Digest(
            AgentHarness.OrchestratorCommand(resume: true, inNvim: true, Launch), new Dictionary<string, string>()),
        "orchestrator-resume-bare" => Digest(
            AgentHarness.OrchestratorCommand(resume: true, inNvim: false, Launch), new Dictionary<string, string>()),
        "resumed-claude" => Digest(
            AgentHarness.Resumed(AgentHarness.CommandFor(AgentHarness.Claude, launch: Launch)),
            new Dictionary<string, string>()),
        "resumed-nvim-with-claude" => Digest(
            AgentHarness.Resumed(AgentHarness.CommandFor(AgentHarness.Nvim, withClaude: true, launch: Launch)),
            new Dictionary<string, string>()),
        "resumed-plain-nvim" => Digest(
            AgentHarness.Resumed(AgentHarness.CommandFor(AgentHarness.Nvim)), new Dictionary<string, string>()),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Claude_commands_and_env_stay_byte_identical(string name, string golden) =>
        Assert.Equal(golden, Actual(name));
}
