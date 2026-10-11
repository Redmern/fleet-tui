namespace Fleet.Shared.Settings.Models;

public sealed record JiraSettings(string BaseUrl, string Email, string Jql, string StoryPointsField)
{
    public static JiraSettings? From(string? baseUrl, string? email, string? jql, string? storyPointsField)
    {
        var url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
        var address = (email ?? string.Empty).Trim();

        if (address.Length == 0
            || !Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || !(parsed.Scheme == "https" || (parsed.Scheme == "http" && parsed.IsLoopback)))
        {
            return null;
        }

        return new JiraSettings(
            url,
            address,
            string.IsNullOrWhiteSpace(jql) ? SettingsDefaults.JiraJql : jql.Trim(),
            string.IsNullOrWhiteSpace(storyPointsField) ? SettingsDefaults.JiraStoryPointsField : storyPointsField.Trim());
    }
}
