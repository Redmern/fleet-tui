namespace Fleet.Features.Repositories.Secrets.Models;

public sealed record SecretsPlan(
    string Repository,
    string Root,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Worktrees);
