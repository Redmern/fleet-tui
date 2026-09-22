using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Shared;

public class ProjectConfigPathsTests
{
    private static readonly string ProjectRoot = Path.Combine(Path.GetTempPath(), "techweb");

    [Fact]
    public void The_config_folder_lives_under_dot_fleet_next_to_orchestrations()
    {
        Assert.Equal(
            Path.Combine(ProjectRoot, ".fleet", "config"), ProjectConfigPaths.Root(ProjectRoot));
    }

    [Fact]
    public void The_instructions_and_readme_files_live_in_the_config_folder()
    {
        var root = ProjectConfigPaths.Root(ProjectRoot);

        Assert.Equal(
            Path.Combine(root, "instructions.md"), ProjectConfigPaths.InstructionsFile(ProjectRoot));
        Assert.Equal(Path.Combine(root, "README.md"), ProjectConfigPaths.ReadmeFile(ProjectRoot));
    }
}
