using Fleet.Ports.Mux.Models;

namespace Fleet.Ports.Mux;

public static class TypedPrompt
{
    public const int EchoLength = 16;

    public static readonly TimeSpan EchoDelay = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan Shortest = TimeSpan.FromMilliseconds(1);

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
        var pause = echoDelay is { } given && given > TimeSpan.Zero ? given : echoDelay is null ? EchoDelay : Shortest;
        var waited = TimeSpan.Zero;

        while (true)
        {
            await mux.SendTextAsync(pane, prompt, ct).ConfigureAwait(false);

            var sinceTyped = TimeSpan.Zero;

            do
            {
                await Task.Delay(pause, ct).ConfigureAwait(false);
                sinceTyped += pause;

                var shown = await mux.GetTextAsync(pane, ct).ConfigureAwait(false);

                if (shown.Contains(probe, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            while (waited + sinceTyped < within && sinceTyped < retry);

            waited += sinceTyped;

            if (waited >= within)
            {
                return false;
            }
        }
    }
}
