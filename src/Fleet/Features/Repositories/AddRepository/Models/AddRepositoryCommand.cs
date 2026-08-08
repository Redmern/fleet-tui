using Fleet.Features.Repositories.AddRepository.Enums;

namespace Fleet.Features.Repositories.AddRepository.Models;

public sealed record AddRepositoryCommand(
    AddRepositoryKind Kind, string ProjectRoot, string Name, string DefaultBranch, string? Url)
{
    public static AddRepositoryCommand CreateNew(
        string projectRoot, string name, string defaultBranch)
        => new(AddRepositoryKind.CreateNew, projectRoot, name, defaultBranch, null);

    public static AddRepositoryCommand CloneFrom(
        string projectRoot, string name, string url, string defaultBranch)
        => new(AddRepositoryKind.CloneUrl, projectRoot, name, defaultBranch, url);
}
