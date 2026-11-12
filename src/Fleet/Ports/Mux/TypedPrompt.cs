using Fleet.Ports.Mux.Models;

namespace Fleet.Ports.Mux;

public static class TypedPrompt
{
    public const int EchoLength = 16;

    public const string ClearLine = "\u0015";

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
        var before = Occurrences(await mux.GetTextAsync(pane, ct).ConfigureAwait(false), probe);
        var typedBefore = false;

        while (true)
        {
            if (typedBefore)
            {
                await mux.SendTextAsync(pane, ClearLine, ct).ConfigureAwait(false);
            }

            await mux.SendTextAsync(pane, prompt, ct).ConfigureAwait(false);
            typedBefore = true;

            var sinceTyped = TimeSpan.Zero;

            do
            {
                await Task.Delay(pause, ct).ConfigureAwait(false);
                sinceTyped += pause;

                var shown = await mux.GetTextAsync(pane, ct).ConfigureAwait(false);

                if (Occurrences(shown, probe) > before)
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

    private static int Occurrences(string text, string probe)
    {
        var count = 0;

        for (var at = text.IndexOf(probe, StringComparison.Ordinal);
            probe.Length > 0 && at >= 0;
            at = text.IndexOf(probe, at + probe.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
