using System.Reflection;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Shared.Settings;

// AC-26: the texts agents read send them to open_pr, and none presents `gh pr create` as the way to open a PR.
public sealed class OpenPrGuidanceTests
{
    private static string RepoRoot { get; } =
        typeof(OpenPrGuidanceTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;

    private static string Skill => File.ReadAllText(Path.Combine(RepoRoot, "skills", "fl-pr", "SKILL.md"));

    [Fact]
    public void The_fl_pr_skill_sends_agents_to_the_open_pr_tool_with_the_body_file()
    {
        Assert.Contains("`open_pr` tool", Skill);
        Assert.Contains("`body_file`", Skill);
        Assert.Contains("Never merge", Skill);
        Assert.DoesNotContain("gh pr create", Skill);
        Assert.DoesNotContain("gh pr", Skill);
    }

    [Fact]
    public void The_fl_pr_skill_names_no_forge_and_nothing_of_one_repository()
    {
        foreach (var word in new[] { "Forgejo", "Gitea", "AGit", "refs/for", "fleet-tui", "redmer" })
        {
            Assert.DoesNotContain(word, Skill, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_git_gates_send_agents_to_open_pr_and_keep_the_rule_that_stops_gh_pr_create()
    {
        Assert.Contains("open_pr", GitGates.PrGuidance);
        Assert.Contains("Do not push for a pull request or run a forge command line yourself", GitGates.PrGuidance);
        Assert.DoesNotContain("gh pr create", GitGates.PrGuidance);

        Assert.Equal("Bash(gh pr create:*)", GitGates.PrCreateRule);
        Assert.True(GitGates.IsOwned(GitGates.PrCreateRule));
    }

    [Fact]
    public void No_text_in_src_or_skills_presents_gh_pr_create_to_an_agent()
    {
        // The two places that may name it: the permission rule that denies it, and the handler that runs gh itself.
        var allowed = new[]
        {
            Path.Combine("src", "Fleet", "Shared", "Settings", "GitGates.cs"),
        };

        var files = Directory
            .EnumerateFiles(Path.Combine(RepoRoot, "src", "Fleet"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(RepoRoot, "skills"), "*.md", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal));

        var naming = files
            .Where(f => File.ReadAllText(f).Contains("gh pr create", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();

        Assert.Equal(allowed, naming);
    }

    [Fact]
    public void Open_pr_is_a_tool_with_an_id_a_description_and_a_place_in_the_settings_table()
    {
        Assert.Equal("open_pr", HarnessToolIds.For(HarnessTool.OpenPr));
        Assert.Equal(HarnessTool.OpenPr, HarnessToolIds.Parse("open_pr"));
        Assert.Equal("Push a branch and open a pull request", SettingsDefaults.Describe(HarnessTool.OpenPr));
        Assert.Contains(HarnessTool.OpenPr, SettingsDefaults.Configurable);
        Assert.True(SettingsDefaults.ServedByProjectMcp(HarnessTool.OpenPr));
        Assert.Equal(new ToolRule(ActionPolicy.Ask, AskChannel.Both), SettingsDefaults.RuleFor(HarnessTool.OpenPr));
        Assert.Equal(ActionPolicy.Ask, SettingsConfig.Default.MergedOverDefaults().RuleFor(HarnessTool.OpenPr).Policy);
    }

    [Theory]
    [InlineData("github", ForgeKind.GitHub)]
    [InlineData(" Forgejo ", ForgeKind.Forgejo)]
    [InlineData("GITEA", ForgeKind.Gitea)]
    [InlineData("gitlab", ForgeKind.Auto)]
    [InlineData("auto", ForgeKind.Auto)]
    [InlineData("", ForgeKind.Auto)]
    [InlineData(null, ForgeKind.Auto)]
    public void A_forge_setting_is_one_of_three_words_and_anything_else_means_detect(string? word, ForgeKind forge) =>
        Assert.Equal(forge, RepositorySettings.From(word, null).Forge);

    [Fact]
    public void The_push_remote_is_origin_unless_the_repository_sets_another()
    {
        Assert.Equal("origin", RepositorySettings.Default.Remote);
        Assert.Equal("origin", RepositorySettings.From(null, "  ").Remote);
        Assert.Equal("origin", new RepositorySettings(ForgeKind.Auto, string.Empty).Remote);
        Assert.Equal("upstream", RepositorySettings.From(null, " upstream ").Remote);
    }

    [Fact]
    public void Repository_settings_are_found_whatever_the_case_and_survive_the_merge_over_the_defaults()
    {
        var config = SettingsConfig.Default
            .WithRepository("Fleet-Tui", RepositorySettings.From("forgejo", null))
            .MergedOverDefaults();

        Assert.Equal(ForgeKind.Forgejo, config.RepositoryFor("fleet-tui").Forge);
        Assert.Equal(RepositorySettings.Default, config.RepositoryFor("other"));
        Assert.Empty(config.WithRepository("FLEET-TUI", RepositorySettings.Default).Repositories);
    }
}
