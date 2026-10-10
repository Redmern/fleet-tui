namespace Fleet.Platform.Forwards;

public static class HealthCheck
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(3) };

    public static async Task<bool> OkAsync(string url, CancellationToken ct)
    {
        try
        {
            using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            ct.ThrowIfCancellationRequested();
            return false;
        }
    }
}
