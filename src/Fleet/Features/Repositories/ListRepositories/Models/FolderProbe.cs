using Fleet.Features.Repositories.ListRepositories.Enums;

namespace Fleet.Features.Repositories.ListRepositories.Models;

public sealed record FolderProbe(string Directory, FolderKind Kind, string DefaultBranch = "");
