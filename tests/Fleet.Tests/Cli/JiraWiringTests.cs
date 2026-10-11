using System.Net;
using Fleet.Cli.Composition;
using Fleet.Platform.Jira;

namespace Fleet.Tests.Cli;

[Collection(ConfigHomeCollection.Name)]
public sealed class JiraWiringTests : ConfigHomeFixture
{
    private string Root => Path.Combine(ConfigHome, "root");

    private void Settings(string baseUrl)
    {
        Directory.CreateDirectory(Path.Combine(ConfigHome, "settings"));
        File.WriteAllText(
            Path.Combine(ConfigHome, "settings", "techweb.json"),
            $$"""{ "jira": { "baseUrl": "{{baseUrl}}", "email": "me@acme.test" } }""");
    }

    private static void Token()
    {
        var file = FileJiraTokenStore.FileFor("techweb");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "secret-token");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public void A_project_without_jira_settings_gets_no_jira_sync()
    {
        using var pulls = new JiraPulls();

        Assert.Null(pulls.For("techweb", Root));
    }

    [Fact]
    public void A_project_with_jira_settings_gets_a_jira_sync()
    {
        Settings("https://acme.atlassian.net");
        using var pulls = new JiraPulls();

        Assert.NotNull(pulls.For("techweb", Root));
    }

    [Fact]
    public async Task A_jira_sync_without_a_token_fails_before_any_request()
    {
        Settings("https://unreachable.invalid");
        using var pulls = new JiraPulls();

        var result = await pulls.For("techweb", Root)!(CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("no Jira API token", result.Error);
    }

    [Fact]
    public async Task Closing_the_board_disposes_the_jira_client_it_made()
    {
        Settings("https://acme.atlassian.net");
        Token();
        var handler = new EmptyResult();
        var pulls = new JiraPulls(() => new JiraRestClient(handler));
        var pull = pulls.For("techweb", Root)!;

        var before = await pull(CancellationToken.None);
        pulls.Dispose();
        var after = await pull(CancellationToken.None);

        Assert.True(before.Succeeded, before.Error);
        Assert.False(after.Succeeded);
        Assert.Equal(1, handler.Requests);
    }

    private sealed class EmptyResult : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{ "issues": [], "isLast": true }"""),
            });
        }
    }
}
