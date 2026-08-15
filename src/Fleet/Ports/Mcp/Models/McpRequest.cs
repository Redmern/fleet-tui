namespace Fleet.Ports.Mcp.Models;

public sealed record McpRequest(string Tool, IReadOnlyDictionary<string, string> Arguments)
{
    public string Value(string key) => Arguments.TryGetValue(key, out var value) ? value : string.Empty;

    public bool Flag(string key) =>
        Arguments.TryGetValue(key, out var value)
        && value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
}
