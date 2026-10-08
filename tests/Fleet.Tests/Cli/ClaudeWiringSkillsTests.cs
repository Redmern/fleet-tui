using Fleet.Cli.Composition;
using Fleet.Platform.Claude;

namespace Fleet.Tests.Cli;

// ResyncWorktree only writes inside the folder. SyncFolder and ApproveFolder also write the user's
// ~/.claude settings and trust file, so they are not called here; all three share the same step.
[Collection(ConfigHomeCollection.Name)]
public sealed class ClaudeWiringSkillsTests : ConfigHomeFixture
{
    private readonly string _worktree = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public ClaudeWiringSkillsTests() => Directory.CreateDirectory(_worktree);

    private string SkillPath(string name) => Path.Combine(_worktree, ".claude", "skills", name, "SKILL.md");

    [Fact]
    public void A_worktree_resync_writes_the_fleet_skills()
    {
        try
        {
            var result = ClaudeWiring.ResyncWorktree("skills-test", _worktree, "fleet-tui", "feat/x");

            Assert.True(result.Succeeded);
            Assert.All(FleetSkills.Names, name => Assert.True(File.Exists(SkillPath(name)), name));
        }
        finally
        {
            TryDelete(_worktree);
        }
    }

    [Fact]
    public void A_failed_resync_writes_no_skills()
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(_worktree, ".claude"));
            File.WriteAllText(Path.Combine(_worktree, ".claude", "settings.local.json"), "{ not json");

            var result = ClaudeWiring.ResyncWorktree("skills-test", _worktree, "fleet-tui", "feat/x");

            Assert.False(result.Succeeded);
            Assert.False(Directory.Exists(Path.Combine(_worktree, ".claude", "skills")));
        }
        finally
        {
            TryDelete(_worktree);
        }
    }
}
