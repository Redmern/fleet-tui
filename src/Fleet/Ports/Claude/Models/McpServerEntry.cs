namespace Fleet.Ports.Claude.Models;

public sealed record McpServerEntry(string Name, string Command, IReadOnlyList<string> Args);
