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
        { "nvim", "5005e6b258d09d4764f0535e22580e00a1dd645e9761ab02e8a656278c332c80" },
        { "nvim-with-claude", "7f812136f199c54890c5af46680c2cf0ca4fa5bd9068090b5bd593127ca5f13c" },
        { "orchestrator-nvim", "2562b3ec6beae1b2091d15f014ecb0dadfed808f1d619fd99606a2c9bb3440e2" },
        { "orchestrator-bare", "9e2809841c1b312e212a402a5de4cbab4bce58b928bf26d5b876c0f3ba0fbdab" },
        { "orchestrator-resume-nvim", "31de4ad35c6e8f4f5ab7e9f3d1b3899e07f49be5e41485ee4fb8f5df153645f9" },
        { "orchestrator-resume-bare", "bce80fece7de036d33a98e3ec1ddca5ec861ed99d2affe13b8aebb7b0f512185" },
        { "resumed-claude", "bce80fece7de036d33a98e3ec1ddca5ec861ed99d2affe13b8aebb7b0f512185" },
        { "resumed-nvim-with-claude", "26d2de35bbe36e8d2795b44ff9cf068c00a889e23eea572e55d263d6c7f66537" },
        { "resumed-plain-nvim", "5005e6b258d09d4764f0535e22580e00a1dd645e9761ab02e8a656278c332c80" },
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
