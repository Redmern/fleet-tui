using Fleet.Cli.Composition;
using Fleet.Platform.Claude;

namespace Fleet.Tests.Platform.Claude;

public sealed class FleetSkillsTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public FleetSkillsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string SkillPath(string name) => Path.Combine(_dir, ".claude", "skills", name, "SKILL.md");

    [Fact]
    public void All_five_skills_are_embedded_with_their_own_name()
    {
        var skills = FleetSkills.Embedded();

        Assert.Equal(FleetSkills.Names.Order(), skills.Keys.Order());

        foreach (var (name, content) in skills)
        {
            Assert.StartsWith("---", content);
            Assert.Contains($"name: {name}\n", content.ReplaceLineEndings("\n"));
        }
    }

    [Fact]
    public void It_writes_every_skill_into_a_new_folder()
    {
        FleetSkills.WriteTo(_dir);

        foreach (var (name, content) in FleetSkills.Embedded())
        {
            Assert.Equal(content.ReplaceLineEndings("\n"), File.ReadAllText(SkillPath(name)));
        }
    }

    [Fact]
    public void A_rerun_with_identical_content_leaves_the_files_alone()
    {
        FleetSkills.WriteTo(_dir);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(SkillPath("fl-tdd"), stamp);

        FleetSkills.WriteTo(_dir);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(SkillPath("fl-tdd")));
    }

    [Fact]
    public void A_changed_fleet_skill_is_overwritten()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SkillPath("fl-pr"))!);
        File.WriteAllText(SkillPath("fl-pr"), "edited by hand");

        FleetSkills.WriteTo(_dir, new Dictionary<string, string> { ["fl-pr"] = "fleet's\r\nversion" });

        Assert.Equal("fleet's\nversion", File.ReadAllText(SkillPath("fl-pr")));
    }

    [Fact]
    public void Other_skills_in_the_folder_are_untouched()
    {
        var mine = SkillPath("my-skill");
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllText(mine, "mine");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(mine)!, "notes.md"), "notes");

        FleetSkills.WriteTo(_dir);

        Assert.Equal("mine", File.ReadAllText(mine));
        Assert.Equal("notes", File.ReadAllText(Path.Combine(Path.GetDirectoryName(mine)!, "notes.md")));
        Assert.Equal(
            FleetSkills.Names.Append("my-skill").Order(),
            Directory.GetDirectories(Path.Combine(_dir, ".claude", "skills")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void A_folder_that_is_not_a_git_repository_still_gets_the_skills()
    {
        Assert.False(Directory.Exists(Path.Combine(_dir, ".git")));

        FleetSkills.WriteTo(_dir);

        Assert.All(FleetSkills.Names, name => Assert.True(File.Exists(SkillPath(name))));
    }

    [Fact]
    public void The_worktree_excludes_cover_the_written_skills()
    {
        Assert.Contains("/.claude/", ClaudeWiring.FleetExcludes);
    }
}
