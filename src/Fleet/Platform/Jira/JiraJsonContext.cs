using System.Text.Json.Serialization;
using Fleet.Platform.Jira.Models;

namespace Fleet.Platform.Jira;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(JiraSearchJson))]
public partial class JiraJsonContext : JsonSerializerContext;
