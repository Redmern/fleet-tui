using Xunit.Sdk;

namespace Fleet.Tests.Support;

// For tests that assert real wall-clock latency (a Stopwatch against a fixed bound), where a busy runner can stall
// one attempt. Anything that can run on an injected clock should do that instead. The attempt must build its own
// rig, so the second try starts clean. Only a failed check (an xunit assertion or a timeout) is retried.
public static class Timing
{
    public static async Task RetryOnceAsync(Func<Task> attempt)
    {
        XunitException? first = null;
        try
        {
            await attempt();
            return;
        }
        catch (Exception e) when (e is XunitException or TimeoutException)
        {
            first = new XunitException($"first try: {e.Message}");
        }

        try
        {
            await attempt();
        }
        catch (Exception e) when (e is XunitException or TimeoutException)
        {
            throw new XunitException($"failed twice.{Environment.NewLine}{first.Message}{Environment.NewLine}second try: {e.Message}", e);
        }
    }
}
