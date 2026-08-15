namespace Fleet.Ports.Mcp.Models;

public sealed record McpResult(string Text, bool IsError)
{
    public static McpResult Ok(string text) => new(text, false);

    public static McpResult Error(string text) => new(text, true);
}
