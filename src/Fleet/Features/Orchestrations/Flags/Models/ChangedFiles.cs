namespace Fleet.Features.Orchestrations.Flags.Models;

public sealed record ChangedFiles(string Repository, string Branch, IReadOnlyCollection<string> Files)
{
    public string Name => $"{Repository.Trim()}/{Branch.Trim()}";
}
