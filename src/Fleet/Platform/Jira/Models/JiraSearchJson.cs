using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fleet.Platform.Jira.Models;

public sealed class JiraSearchJson
{
    public List<JiraIssueJson> Issues { get; set; } = [];

    public string? NextPageToken { get; set; }

    public bool? IsLast { get; set; }
}

public sealed class JiraIssueJson
{
    public string Key { get; set; } = string.Empty;

    public JiraFieldsJson? Fields { get; set; }
}

public sealed class JiraFieldsJson
{
    public string? Summary { get; set; }

    public JsonElement Description { get; set; }

    public JiraStatusJson? Status { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Other { get; set; } = [];
}

public sealed class JiraStatusJson
{
    public JiraStatusCategoryJson? StatusCategory { get; set; }
}

public sealed class JiraStatusCategoryJson
{
    public string? Key { get; set; }
}
