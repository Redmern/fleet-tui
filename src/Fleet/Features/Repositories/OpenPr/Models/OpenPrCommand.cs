using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Repositories.OpenPr.Models;

public sealed record OpenPrCommand(
    string Repository,
    string Container,
    string Branch,
    string Base,
    string Title,
    string BodyFile,
    RepositorySettings Settings,
    string Root);
