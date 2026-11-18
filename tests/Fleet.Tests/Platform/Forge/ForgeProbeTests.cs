using System.Net;
using System.Text;
using Fleet.Platform.Forge;
using Fleet.Ports.Forge;
using Fleet.Ports.Forge.Enums;

namespace Fleet.Tests.Platform.Forge;

public sealed class ForgeProbeTests : IDisposable
{
    // Captured 2026-11-17 with one unauthenticated `curl -s -i https://git.home.redmonet.nl/api/v1/version`:
    // HTTP/2 403, content-type application/json;charset=utf-8, and this body. That Forgejo requires sign-in, so
    // it never shows its version to fleet.
    private const string SignInOnlyBody = """{"message":"Only signed in user is allowed to call APIs."}""";

    // Not captured (the host above does not give it): the documented answer of an open Forgejo and of an open Gitea.
    private const string ForgejoBody = """{"version":"11.0.3+gitea-1.22.0"}""";

    private const string GiteaBody = """{"version":"1.22.3"}""";

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "fleet-forge-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string CacheFile => Path.Combine(_folder, "forges.json");

    [Theory]
    [InlineData(HttpStatusCode.OK, ForgejoBody, ForgeProbeResult.ForgejoOrGitea)]
    [InlineData(HttpStatusCode.OK, GiteaBody, ForgeProbeResult.ForgejoOrGitea)]
    [InlineData(HttpStatusCode.Forbidden, SignInOnlyBody, ForgeProbeResult.ForgejoOrGitea)]
    [InlineData(HttpStatusCode.Unauthorized, SignInOnlyBody, ForgeProbeResult.ForgejoOrGitea)]
    [InlineData(HttpStatusCode.OK, "<html>welcome</html>", ForgeProbeResult.NotAForge)]
    [InlineData(HttpStatusCode.OK, """{"version":""}""", ForgeProbeResult.NotAForge)]
    [InlineData(HttpStatusCode.OK, """{"version":3}""", ForgeProbeResult.NotAForge)]
    [InlineData(HttpStatusCode.OK, """["version"]""", ForgeProbeResult.NotAForge)]
    [InlineData(HttpStatusCode.Forbidden, """{"message":"Forbidden"}""", ForgeProbeResult.NotAForge)]
    [InlineData(HttpStatusCode.Forbidden, "", ForgeProbeResult.NotAForge)]
    [InlineData(HttpStatusCode.NotFound, ForgejoBody, ForgeProbeResult.NotAForge)]
    [InlineData(HttpStatusCode.Found, "", ForgeProbeResult.Unreachable)]
    [InlineData(HttpStatusCode.BadGateway, "", ForgeProbeResult.Unreachable)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ForgejoBody, ForgeProbeResult.Unreachable)]
    [InlineData(HttpStatusCode.TooManyRequests, "", ForgeProbeResult.Unreachable)]
    public void A_version_answer_or_the_sign_in_only_answer_marks_forgejo_or_gitea(
        HttpStatusCode status, string body, ForgeProbeResult expected) =>
        Assert.Equal(expected, ForgeVersionAnswer.Read(status, body));

    [Fact]
    public async Task The_probe_asks_the_version_api_of_the_host_over_https_without_credentials()
    {
        var web = new FakeWeb(_ => Answer(HttpStatusCode.Forbidden, SignInOnlyBody));

        var result = await new HttpForgeProbe(web).ProbeAsync("git.home.redmonet.nl");

        Assert.Equal(ForgeProbeResult.ForgejoOrGitea, result);

        var request = Assert.Single(web.Requests);

        Assert.Equal(("GET", "https://git.home.redmonet.nl/api/v1/version"), request);
        Assert.False(web.SentCredentials);
    }

    [Fact]
    public async Task A_host_that_fails_or_does_not_answer_in_time_is_unreachable()
    {
        var refusing = new HttpForgeProbe(new FakeWeb(_ => throw new HttpRequestException("connection refused")));
        var silent = new HttpForgeProbe(new FakeWeb(_ => null), TimeSpan.FromMilliseconds(50));

        Assert.Equal(ForgeProbeResult.Unreachable, await refusing.ProbeAsync("192.168.0.213"));
        Assert.Equal(ForgeProbeResult.Unreachable, await silent.ProbeAsync("git.example.test"));
    }

    [Fact]
    public async Task A_cancelled_call_is_cancelled_not_called_unreachable()
    {
        using var cancelled = new CancellationTokenSource();
        var probe = new HttpForgeProbe(new FakeWeb(_ => null));

        var pending = probe.ProbeAsync("git.example.test", cancelled.Token);

        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a host")]
    [InlineData("evil/../path")]
    [InlineData("git.example.test:port")]
    [InlineData("user@git.example.test")]
    public async Task Something_that_is_no_host_name_is_not_asked(string host)
    {
        var web = new FakeWeb(_ => Answer(HttpStatusCode.OK, ForgejoBody));

        Assert.Equal(ForgeProbeResult.NotAForge, await new HttpForgeProbe(web).ProbeAsync(host));
        Assert.Empty(web.Requests);
    }

    [Theory]
    [InlineData(ForgeProbeResult.ForgejoOrGitea)]
    [InlineData(ForgeProbeResult.NotAForge)]
    public async Task A_host_that_answered_is_asked_once_even_by_a_later_process(ForgeProbeResult answer)
    {
        var inner = new CountingProbe(answer);
        var first = new CachedForgeProbe(inner, () => CacheFile);

        Assert.Equal(answer, await first.ProbeAsync("Git.Example.test"));
        Assert.Equal(answer, await first.ProbeAsync("git.example.test"));

        var later = new CachedForgeProbe(inner, () => CacheFile);

        Assert.Equal(answer, await later.ProbeAsync("git.example.test"));
        Assert.Equal(1, inner.Asked);

        await later.ProbeAsync("other.example.test");

        Assert.Equal(2, inner.Asked);
    }

    [Fact]
    public async Task A_host_that_did_not_answer_is_not_remembered_and_is_asked_again()
    {
        var inner = new CountingProbe(ForgeProbeResult.Unreachable);
        var probe = new CachedForgeProbe(inner, () => CacheFile);

        await probe.ProbeAsync("git.example.test");
        await probe.ProbeAsync("git.example.test");

        Assert.Equal(2, inner.Asked);
        Assert.False(File.Exists(CacheFile));

        inner.Answer = ForgeProbeResult.ForgejoOrGitea;

        Assert.Equal(ForgeProbeResult.ForgejoOrGitea, await probe.ProbeAsync("git.example.test"));
        Assert.Equal(ForgeProbeResult.ForgejoOrGitea, await probe.ProbeAsync("git.example.test"));
        Assert.Equal(3, inner.Asked);
        Assert.Empty(Directory.EnumerateFiles(_folder, "*.tmp"));
    }

    [Fact]
    public async Task A_forge_on_another_port_is_asked_on_that_port()
    {
        var web = new FakeWeb(_ => Answer(HttpStatusCode.OK, GiteaBody));

        Assert.Equal(ForgeProbeResult.ForgejoOrGitea, await new HttpForgeProbe(web).ProbeAsync("git.example.test:3000"));
        Assert.Equal(("GET", "https://git.example.test:3000/api/v1/version"), Assert.Single(web.Requests));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":1,"hosts":null}""")]
    [InlineData("""{"version":1,"hosts":{"git.example.test":"something-else"}}""")]
    public async Task A_broken_cache_file_is_ignored_and_the_host_is_asked(string content)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(CacheFile, content);

        var inner = new CountingProbe(ForgeProbeResult.ForgejoOrGitea);

        Assert.Equal(
            ForgeProbeResult.ForgejoOrGitea,
            await new CachedForgeProbe(inner, () => CacheFile).ProbeAsync("git.example.test"));
        Assert.Equal(1, inner.Asked);
        Assert.Contains("\"git.example.test\": \"forgejo-or-gitea\"", File.ReadAllText(CacheFile));
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class CountingProbe(ForgeProbeResult answer) : IForgeProbe
    {
        public ForgeProbeResult Answer { get; set; } = answer;

        public int Asked { get; private set; }

        public Task<ForgeProbeResult> ProbeAsync(string host, CancellationToken ct = default)
        {
            Asked++;

            return Task.FromResult(Answer);
        }
    }

    // A null answer never comes: the call waits until it is cancelled.
    private sealed class FakeWeb(Func<HttpRequestMessage, HttpResponseMessage?> answer) : HttpMessageHandler
    {
        public List<(string Method, string Url)> Requests { get; } = [];

        public bool SentCredentials { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add((request.Method.Method, request.RequestUri!.ToString()));
            SentCredentials |= request.Headers.Authorization is not null || request.Headers.Contains("Cookie");

            if (answer(request) is { } response)
            {
                return response;
            }

            await Task.Delay(Timeout.Infinite, ct);

            throw new InvalidOperationException();
        }
    }
}
