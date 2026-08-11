using Fleet.Ui.Models;

namespace Fleet.Features.Repositories;

public static class RepositoryChores
{
    public const int DefaultBranch = 0;

    public const int Pull = 1;

    public const int Remove = 2;

    public const int Secrets = 3;

    public static IReadOnlyList<string> Choices { get; } =
    [
        "Change the default branch",
        "Pull the default branch to latest",
        "Remove the repository from this project",
        "Files every worktree needs but git does not carry",
    ];

    public static IReadOnlyList<PickerEntry> Entries { get; } =
    [
        new("branch", Choices[DefaultBranch]),
        new("pull", Choices[Pull]),
        new("remove", Choices[Remove]),
        new("secrets", Choices[Secrets]),
    ];
}
