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
        { "nvim", "c40e7131dafbd0a002ba3987dc5961f3c3aebb03fc0cab4e0adce56f2826e67b" },
        { "nvim-with-claude", "87ea0a19fd2c0257acd844e6cd6b5a4863440a646f9645252f851c3adf976f91" },
        { "orchestrator-nvim", "fe425199f7a2e7941097f650c1b9aa3ef8a7b2f4bf95c243f8d01a70bc9dbabb" },
        { "orchestrator-bare", "9e2809841c1b312e212a402a5de4cbab4bce58b928bf26d5b876c0f3ba0fbdab" },
        { "orchestrator-resume-nvim", "b7ee989668a0372dcaa998fe66240eef3b7606a9dacf09f18a4fafcebd2ad191" },
        { "orchestrator-resume-bare", "bce80fece7de036d33a98e3ec1ddca5ec861ed99d2affe13b8aebb7b0f512185" },
        { "resumed-claude", "bce80fece7de036d33a98e3ec1ddca5ec861ed99d2affe13b8aebb7b0f512185" },
        { "resumed-nvim-with-claude", "29b1cbae80086e8401a4fec7de6a2cba2c668380e5b3e46abe0cd0118f132d64" },
        { "resumed-plain-nvim", "c40e7131dafbd0a002ba3987dc5961f3c3aebb03fc0cab4e0adce56f2826e67b" },
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
