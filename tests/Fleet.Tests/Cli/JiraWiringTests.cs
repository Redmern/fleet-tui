using Fleet.Cli.Composition;

namespace Fleet.Tests.Cli;

[Collection(ConfigHomeCollection.Name)]
public sealed class JiraWiringTests : ConfigHomeFixture
{
    [Fact]
    public void A_project_without_jira_settings_gets_no_jira_sync()
    {
        Assert.Null(Adapters.JiraSync("techweb", Path.Combine(ConfigHome, "root")));
    }

    [Fact]
    public void A_project_with_jira_settings_gets_a_jira_sync()
    {
        Directory.CreateDirectory(Path.Combine(ConfigHome, "settings"));
        File.WriteAllText(
            Path.Combine(ConfigHome, "settings", "techweb.json"),
            """{ "jira": { "baseUrl": "https://acme.atlassian.net", "email": "me@acme.test" } }""");

        Assert.NotNull(Adapters.JiraSync("techweb", Path.Combine(ConfigHome, "root")));
    }

    [Fact]
    public async Task A_jira_sync_without_a_token_fails_before_any_request()
    {
        Directory.CreateDirectory(Path.Combine(ConfigHome, "settings"));
        File.WriteAllText(
            Path.Combine(ConfigHome, "settings", "techweb.json"),
            """{ "jira": { "baseUrl": "https://unreachable.invalid", "email": "me@acme.test" } }""");

        var result = await Adapters.JiraSync("techweb", Path.Combine(ConfigHome, "root"))!();

        Assert.False(result.Succeeded);
        Assert.Contains("no Jira API token", result.Error);
    }
}
