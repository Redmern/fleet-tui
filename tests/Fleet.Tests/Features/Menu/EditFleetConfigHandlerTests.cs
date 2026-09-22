using Fleet.Features.Menu.EditFleetConfig;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Features.Menu;

public sealed class EditFleetConfigHandlerTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public EditFleetConfigHandlerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void It_seeds_the_config_folder_with_a_default_instructions_file_and_a_readme()
    {
        var folder = EditFleetConfigHandler.Ensure(_root);

        Assert.Equal(ProjectConfigPaths.Root(_root), folder);
        Assert.True(Directory.Exists(folder));

        var instructions = File.ReadAllText(ProjectConfigPaths.InstructionsFile(_root));
        Assert.Contains(OrchestrationText.DefaultHowYouWork, instructions);

        Assert.True(File.Exists(ProjectConfigPaths.ReadmeFile(_root)));
    }

    [Fact]
    public void It_never_overwrites_an_instructions_file_the_user_already_edited()
    {
        EditFleetConfigHandler.Ensure(_root);
        File.WriteAllText(ProjectConfigPaths.InstructionsFile(_root), "Only ever touch api/.");

        EditFleetConfigHandler.Ensure(_root);

        Assert.Equal(
            "Only ever touch api/.", File.ReadAllText(ProjectConfigPaths.InstructionsFile(_root)));
    }

    [Fact]
    public void It_never_overwrites_a_readme_the_user_already_edited()
    {
        EditFleetConfigHandler.Ensure(_root);
        File.WriteAllText(ProjectConfigPaths.ReadmeFile(_root), "my notes");

        EditFleetConfigHandler.Ensure(_root);

        Assert.Equal("my notes", File.ReadAllText(ProjectConfigPaths.ReadmeFile(_root)));
    }
}
