namespace Fleet.Features.Head.ServeHead.Models;

public sealed record HeadToolParam(string Name, string Type, string Description, bool Required);

public sealed record HeadToolSpec(string Name, string Description, IReadOnlyList<HeadToolParam> Params);
