using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Mcp.ServeMcp.Models;

public sealed record ToolParam(string Name, string Type, string Description, bool Required);

public sealed record ToolSpec(
    HarnessTool Tool,
    string Name,
    string Description,
    IReadOnlyList<ToolParam> Params);
