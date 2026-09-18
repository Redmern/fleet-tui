namespace Fleet.Platform.Mux.WezTerm;

public sealed class WezTermInstanceLauncher(
    Func<CancellationToken, Task<bool>> reachable, Action<string> start)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);

    public async Task<bool> EnsureRunningAsync(
        string configFile, TimeSpan timeout, CancellationToken ct = default)
    {
        if (await reachable(ct).ConfigureAwait(false))
        {
            return true;
        }

        start(configFile);

        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, ct).ConfigureAwait(false);

            if (await reachable(ct).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }
}
