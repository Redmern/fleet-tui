using Fleet.Shared.Orchestrations;

namespace Fleet.Features.Menu.EditFleetConfig;

public static class EditFleetConfigHandler
{
    private const string ReadmeText =
        """
        # Fleet config

        Fleet reads these files when it works with this project. Nothing here is
        required — delete a file to go back to fleet's built-in default.

        - instructions.md — replaces the "How you work" section fleet writes into
          CLAUDE.md when it dispatches a sub-orchestrator for this project. Add
          project-specific conventions here.
        - ai-dlc.md — extra guidance for the Ai-DLC process. fleet writes the
          "## Process" section of CLAUDE.md itself from the task's profile and
          this project's Ai-DLC settings (the Ai-DLC item in the fleet menu's
          Settings submenu); create this file to append your own notes to it.
          The older name aidlc.md still works; if both exist, ai-dlc.md wins.
        """;

    public static string Ensure(string projectRoot)
    {
        var folder = ProjectConfigPaths.Root(projectRoot);

        Directory.CreateDirectory(folder);

        var instructions = ProjectConfigPaths.InstructionsFile(projectRoot);

        if (!File.Exists(instructions))
        {
            File.WriteAllText(instructions, OrchestrationText.DefaultHowYouWork + "\n");
        }


        var readme = ProjectConfigPaths.ReadmeFile(projectRoot);

        if (!File.Exists(readme))
        {
            File.WriteAllText(readme, ReadmeText);
        }

        return folder;
    }
}
