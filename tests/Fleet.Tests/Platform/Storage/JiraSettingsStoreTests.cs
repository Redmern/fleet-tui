using Fleet.Platform.Storage;
using Fleet.Shared.Settings;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JiraSettingsStoreTests : ConfigHomeFixture
{
    private static JsonSettingsStore Store => new();

    private string SettingsFile => Path.Combine(ConfigHome, "settings", "techweb.json");

    private void Write(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, json);
    }

    [Fact]
    public void A_project_without_jira_settings_has_jira_off()
    {
        Assert.Null(Store.Load("techweb").Jira);
        Assert.Null(SettingsConfig.Default.Jira);
    }

    [Fact]
    public void Jira_settings_are_read_with_the_default_query_and_story_points_field()
    {
        Write("""{ "jira": { "baseUrl": "https://acme.atlassian.net/", "email": "me@acme.test" } }""");

        var jira = Store.Load("techweb").Jira;

        Assert.NotNull(jira);
        Assert.Equal("https://acme.atlassian.net", jira.BaseUrl);
        Assert.Equal("me@acme.test", jira.Email);
        Assert.Equal("assignee = currentUser() AND resolution = EMPTY", jira.Jql);
        Assert.Equal(SettingsDefaults.JiraJql, jira.Jql);
        Assert.Equal("customfield_10016", jira.StoryPointsField);
    }

    [Fact]
    public void A_custom_query_and_story_points_field_are_kept()
    {
        Write("""{ "jira": { "baseUrl": "https://acme.atlassian.net", "email": "me@acme.test", "jql": "project = WEB", "storyPointsField": "customfield_10026" } }""");

        var jira = Store.Load("techweb").Jira!;

        Assert.Equal("project = WEB", jira.Jql);
        Assert.Equal("customfield_10026", jira.StoryPointsField);
    }

    [Theory]
    [InlineData("""{ "jira": { "email": "me@acme.test" } }""")]
    [InlineData("""{ "jira": { "baseUrl": "https://acme.atlassian.net" } }""")]
    [InlineData("""{ "jira": { "baseUrl": "not a url", "email": "me@acme.test" } }""")]
    [InlineData("""{ "jira": { "baseUrl": "ftp://acme.test", "email": "me@acme.test" } }""")]
    [InlineData("""{ "jira": { "baseUrl": "http://acme.atlassian.net", "email": "me@acme.test" } }""")]
    public void Incomplete_or_invalid_jira_settings_leave_jira_off(string json)
    {
        Write(json);

        Assert.Null(Store.Load("techweb").Jira);
    }

    [Fact]
    public void Saving_other_settings_keeps_the_jira_settings()
    {
        Write("""{ "jira": { "baseUrl": "https://acme.atlassian.net", "email": "me@acme.test", "jql": "project = WEB" } }""");

        Store.Save("techweb", Store.Load("techweb").WithTrigger(";"));

        var loaded = Store.Load("techweb");
        Assert.Equal(";", loaded.Trigger);
        Assert.Equal(new JiraSettings("https://acme.atlassian.net", "me@acme.test", "project = WEB", "customfield_10016"), loaded.Jira);
    }

    [Fact]
    public void Plain_http_is_only_accepted_for_a_local_jira() =>
        Assert.NotNull(JiraSettings.From("http://localhost:8080", "me@acme.test", null, null));

    [Fact]
    public void Saving_other_settings_keeps_an_invalid_jira_block_for_the_user_to_fix()
    {
        Write("""{ "jira": { "baseUrl": "acme.atlassian.net", "email": "me@acme.test" } }""");

        Store.Save("techweb", Store.Load("techweb").WithTrigger(";"));

        var text = File.ReadAllText(SettingsFile);
        Assert.Contains("acme.atlassian.net", text);
        Assert.Contains("me@acme.test", text);
    }

    [Fact]
    public void Saving_without_jira_writes_no_jira_key()
    {
        Store.Save("techweb", SettingsConfig.Default.WithTrigger(";"));

        Assert.DoesNotContain("jira", File.ReadAllText(SettingsFile), StringComparison.OrdinalIgnoreCase);
    }
}
