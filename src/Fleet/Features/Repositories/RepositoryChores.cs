namespace Fleet.Features.Repositories;

public static class RepositoryChores
{
    public const int DefaultBranch = 0;

    public static IReadOnlyList<string> Choices { get; } =
    [
        "Change the default branch",
    ];
}
