using Fleet.Ports.Mux.Models;

namespace Fleet.Ports.Mux;

public static class TypedPrompt
{
    public const int EchoLength = 16;

    public static readonly TimeSpan EchoDelay = TimeSpan.FromMilliseconds(400);

    public static async Task<bool> UntilEchoedAsync(
        IMuxDriver mux,
        PaneId pane,
        string prompt,
        TimeSpan retry,
        TimeSpan within,
        TimeSpan? echoDelay = null,
        CancellationToken ct = default)
    {
        var probe = prompt[..Math.Min(EchoLength, prompt.Length)];
        var pause = echoDelay ?? EchoDelay;
        var waited = TimeSpan.Zero;

        while (true)
        {
            await mux.SendTextAsync(pane, prompt, ct).ConfigureAwait(false);
            await Task.Delay(pause, ct).ConfigureAwait(false);

            var shown = await mux.GetTextAsync(pane, ct).ConfigureAwait(false);

            if (shown.Contains(probe, StringComparison.Ordinal))
            {
                return true;
            }

            if (waited >= within)
            {
                return false;
            }

            await Task.Delay(retry, ct).ConfigureAwait(false);
            waited += pause + retry;
        }
    }
}
