using System.Text.Json;
using Fleet.Platform.Mcp.Models;

namespace Fleet.Platform.Mcp;

public sealed record SchemaField(string Name, string Type, string Description, bool Required);

public static class ToolSchema
{
    public static string For(IReadOnlyList<SchemaField> fields)
    {
        var schema = new SchemaObject();

        foreach (var field in fields)
        {
            schema.Properties[field.Name] = new SchemaProperty
            {
                Type = field.Type,
                Description = field.Description,
            };

            if (field.Required)
            {
                schema.Required.Add(field.Name);
            }
        }

        return JsonSerializer.Serialize(schema, McpJsonContext.Default.SchemaObject);
    }
}
