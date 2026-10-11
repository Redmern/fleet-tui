using System.Net;
using System.Text;
using Fleet.Platform.Jira;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Platform.Jira;

public class JiraRestClientTests
{
    private const string Token = "s3cret-token";

    private static readonly JiraSettings Settings =
        new("https://acme.atlassian.net", "me@acme.test", "assignee = currentUser() AND resolution = EMPTY", "customfield_10016");

    private readonly FakeHandler _handler = new();

    private JiraRestClient Client => new(_handler);

    private static string Page(string issues, string tail = "\"isLast\": true") =>
        "{ \"issues\": [" + issues + "], " + tail + " }";

    private static string Issue(string key, string summary, string category = "new", string points = "3") =>
        "{ \"id\": \"1\", \"key\": \"" + key + "\", \"fields\": { \"summary\": \"" + summary + "\", "
        + "\"description\": { \"type\": \"doc\", \"version\": 1, \"content\": [{ \"type\": \"paragraph\", \"content\": [{ \"type\": \"text\", \"text\": \"why " + key + "\" }] }] }, "
        + "\"status\": { \"name\": \"Whatever\", \"statusCategory\": { \"key\": \"" + category + "\" } }, "
        + "\"customfield_10016\": " + points + " } }";

    [Fact]
    public async Task It_searches_with_a_get_and_basic_auth()
    {
        _handler.Reply(HttpStatusCode.OK, Page(string.Empty));

        await Client.SearchAsync(Settings, Token);

        var request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("https", request.Uri.Scheme);
        Assert.Equal("acme.atlassian.net", request.Uri.Host);
        Assert.Equal("/rest/api/3/search/jql", request.Uri.AbsolutePath);
        Assert.Contains("jql=assignee%20%3D%20currentUser%28%29%20AND%20resolution%20%3D%20EMPTY", request.Uri.Query);
        Assert.Contains("fields=summary%2Cdescription%2Cstatus%2Ccustomfield_10016", request.Uri.Query);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("me@acme.test:" + Token)), request.Authorization);
    }

    [Fact]
    public async Task Issues_map_to_key_summary_text_points_and_done()
    {
        _handler.Reply(HttpStatusCode.OK, Page(Issue("WEB-1", "Fix login") + "," + Issue("WEB-2", "Ship it", "done", "null")));

        var result = await Client.SearchAsync(Settings, Token);

        Assert.True(result.Succeeded, result.Error);
        Assert.Collection(
            result.Value,
            first =>
            {
                Assert.Equal("WEB-1", first.Key);
                Assert.Equal("Fix login", first.Summary);
                Assert.Equal("why WEB-1", first.Description);
                Assert.Equal(3.0, first.StoryPoints);
                Assert.False(first.Done);
            },
            second =>
            {
                Assert.Null(second.StoryPoints);
                Assert.True(second.Done);
            });
    }

    [Fact]
    public async Task The_story_points_field_comes_from_the_settings()
    {
        _handler.Reply(HttpStatusCode.OK, Page("{ \"key\": \"WEB-1\", \"fields\": { \"summary\": \"x\", \"customfield_10026\": 5.0, \"customfield_10016\": 1 } }"));

        var result = await Client.SearchAsync(Settings with { StoryPointsField = "customfield_10026" }, Token);

        Assert.Equal(5.0, Assert.Single(result.Value).StoryPoints);
        Assert.Contains("customfield_10026", _handler.Requests[0].Uri.Query);
    }

    [Fact]
    public async Task It_follows_the_next_page_token()
    {
        _handler.Reply(HttpStatusCode.OK, Page(Issue("WEB-1", "a"), "\"nextPageToken\": \"tok/2\", \"isLast\": false"));
        _handler.Reply(HttpStatusCode.OK, Page(Issue("WEB-2", "b")));

        var result = await Client.SearchAsync(Settings, Token);

        Assert.Equal(["WEB-1", "WEB-2"], result.Value.Select(i => i.Key));
        Assert.Equal(2, _handler.Requests.Count);
        Assert.DoesNotContain("nextPageToken", _handler.Requests[0].Uri.Query);
        Assert.Contains("nextPageToken=tok%2F2", _handler.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task A_query_that_never_ends_fails_instead_of_returning_part_of_it()
    {
        for (var i = 0; i < 60; i++)
        {
            _handler.Reply(HttpStatusCode.OK, Page(Issue("WEB-" + i, "a"), "\"nextPageToken\": \"more\", \"isLast\": false"));
        }

        var result = await Client.SearchAsync(Settings, Token);

        Assert.False(result.Succeeded);
        Assert.Contains("narrow the jql", result.Error);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Rejected_credentials_give_a_clear_error_without_the_token(HttpStatusCode code)
    {
        _handler.Reply(code, "{ \"errorMessages\": [\"nope\"] }");

        var result = await Client.SearchAsync(Settings, Token);

        Assert.False(result.Succeeded);
        Assert.Contains("rejected the credentials", result.Error);
        Assert.Contains(((int)code).ToString(System.Globalization.CultureInfo.InvariantCulture), result.Error);
        Assert.DoesNotContain(Token, result.Error);
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes("me@acme.test:" + Token)), result.Error);
    }

    [Theory]
    [InlineData("AUTHENTICATED_FAILED")]
    [InlineData("AUTHENTICATION_DENIED")]
    public async Task An_anonymous_answer_after_failed_login_counts_as_rejected(string reason)
    {
        _handler.Reply(HttpStatusCode.OK, Page(string.Empty), ("X-Seraph-LoginReason", reason));

        var result = await Client.SearchAsync(Settings, Token);

        Assert.False(result.Succeeded);
        Assert.Contains("rejected the credentials", result.Error);
    }

    [Fact]
    public async Task A_bad_query_shows_jiras_error_messages()
    {
        _handler.Reply(HttpStatusCode.BadRequest, "{ \"errorMessages\": [\"Field 'asignee' does not exist.\"], \"errors\": {} }");

        var result = await Client.SearchAsync(Settings, Token);

        Assert.False(result.Succeeded);
        Assert.Contains("HTTP 400", result.Error);
        Assert.Contains("Field 'asignee' does not exist.", result.Error);
    }

    [Fact]
    public async Task A_cancelled_pull_throws_instead_of_reporting_a_timeout()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        _handler.Reply(HttpStatusCode.OK, Page(string.Empty));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client.SearchAsync(Settings, Token, cancel.Token));
    }

    [Fact]
    public async Task A_server_error_names_the_status()
    {
        _handler.Reply(HttpStatusCode.InternalServerError, "boom");

        var result = await Client.SearchAsync(Settings, Token);

        Assert.False(result.Succeeded);
        Assert.Contains("HTTP 500", result.Error);
    }

    [Fact]
    public async Task An_unreachable_server_is_a_failed_result_without_the_token()
    {
        _handler.Throw(new HttpRequestException("Name or service not known (acme.atlassian.net:443)"));

        var result = await Client.SearchAsync(Settings, Token);

        Assert.False(result.Succeeded);
        Assert.Contains("unreachable", result.Error);
        Assert.DoesNotContain(Token, result.Error);
    }

    private static string Credentials => Convert.ToBase64String(Encoding.UTF8.GetBytes("me@acme.test:" + Token));

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task An_error_answer_that_echoes_the_credentials_never_shows_them(HttpStatusCode code)
    {
        _handler.Reply(code, "{ \"errorMessages\": [\"bad header Basic " + Credentials + " for token " + Token + "\"] }");

        var result = await Client.SearchAsync(Settings, Token);

        Assert.False(result.Succeeded);
        Assert.DoesNotContain(Token, result.Error);
        Assert.DoesNotContain(Credentials, result.Error);
    }

    [Fact]
    public async Task A_network_failure_that_names_the_credentials_never_shows_them()
    {
        _handler.Throw(new HttpRequestException($"proxy refused Authorization: Basic {Credentials} ({Token})"));

        var result = await Client.SearchAsync(Settings, Token);

        Assert.False(result.Succeeded);
        Assert.Contains("unreachable", result.Error);
        Assert.DoesNotContain(Token, result.Error);
        Assert.DoesNotContain(Credentials, result.Error);
    }

    [Fact]
    public async Task An_answer_that_is_not_json_is_a_failed_result()
    {
        _handler.Reply(HttpStatusCode.OK, "<html>login</html>");

        var result = await Client.SearchAsync(Settings, Token);

        Assert.False(result.Succeeded);
        Assert.Contains("could not read", result.Error);
    }

    private sealed record Seen(HttpMethod Method, Uri Uri, string Authorization);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpResponseMessage>> _replies = new();

        public List<Seen> Requests { get; } = [];

        public void Reply(HttpStatusCode code, string body, params (string Name, string Value)[] headers) =>
            _replies.Enqueue(() =>
            {
                var response = new HttpResponseMessage(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

                foreach (var (name, value) in headers)
                {
                    response.Headers.TryAddWithoutValidation(name, value);
                }

                return response;
            });

        public void Throw(Exception e) => _replies.Enqueue(() => throw e);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Add(new Seen(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString() ?? string.Empty));
            return Task.FromResult(_replies.Dequeue()());
        }
    }
}
