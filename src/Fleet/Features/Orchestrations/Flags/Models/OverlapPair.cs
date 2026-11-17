namespace Fleet.Features.Orchestrations.Flags.Models;

public sealed record OverlapPair(string First, string Second, IReadOnlyList<string> Files)
{
    public string Key => $"{First}|{Second}";
}
