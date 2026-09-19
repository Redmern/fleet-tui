using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Fleet.Platform.Releases.Models;
using Fleet.Ports.Releases;
using Fleet.Ports.Releases.Models;
using Fleet.Shared.Constants;

namespace Fleet.Platform.Releases;

public sealed class HttpReleaseClient : IReleaseClient, IDisposable
{
    private readonly HttpClient _http;

    public HttpReleaseClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("fleet", FleetVersion.Current));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseInfo?> LatestAsync(string repo, CancellationToken ct = default) =>
        await FetchAsync($"https://api.github.com/repos/{repo}/releases/latest", ct)
            .ConfigureAwait(false);

    public async Task<ReleaseInfo?> ForVersionAsync(
        string repo, string version, CancellationToken ct = default)
    {
        var tag = version.TrimStart('v', 'V');

        var release = await FetchAsync(
            $"https://api.github.com/repos/{repo}/releases/tags/{tag}", ct).ConfigureAwait(false);

        return release ?? await FetchAsync(
            $"https://api.github.com/repos/{repo}/releases/tags/v{tag}", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ReleaseInfo>> ListAsync(
        string repo, CancellationToken ct = default)
    {
        try
        {
            using var response = await _http
                .GetAsync(
                    $"https://api.github.com/repos/{repo}/releases?per_page=100", ct)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var releases = await JsonSerializer
                .DeserializeAsync(body, GitHubJsonContext.Default.GitHubReleaseJsonArray, ct)
                .ConfigureAwait(false);

            return releases is null
                ? []
                : releases.Where(r => !r.Draft && !string.IsNullOrEmpty(r.TagName))
                    .Select(ToInfo)
                    .ToList();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return [];
        }
    }

    private async Task<ReleaseInfo?> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            var release = await JsonSerializer
                .DeserializeAsync(body, GitHubJsonContext.Default.GitHubReleaseJson, ct)
                .ConfigureAwait(false);

            return release is null || string.IsNullOrEmpty(release.TagName)
                ? null
                : ToInfo(release);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    private static ReleaseInfo ToInfo(GitHubReleaseJson release) =>
        new(
            release.TagName,
            release.Assets.Select(a => new ReleaseAsset(a.Name, a.BrowserDownloadUrl)).ToList(),
            release.Prerelease);

    public async Task<byte[]?> DownloadAsync(string url, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetByteArrayAsync(url, ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}
