namespace Fleet.Features.Head.ServeHead.Models;

public sealed record ProjectStructure(IReadOnlyList<string> Repositories, string Subs, string Agents);
