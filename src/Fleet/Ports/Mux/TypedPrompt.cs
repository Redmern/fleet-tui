using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Ports.Mux;

public static class TypedPrompt
{
    public const int EchoLength = AgentHarness.EchoProbeLength;

    public const string ClearLine = "\u0015";

    public static readonly TimeSpan EchoDelay = TimeSpan.FromMilliseconds(400);

    private static readonly TimeSpan Shortest = TimeSpan.FromMilliseconds(1);

    public static async Task<PromptEcho> UntilEchoedAsync(
        IMuxDriver mux,
        PaneId pane,
        string prompt,
        TimeSpan retry,
        TimeSpan within,
        TimeSpan? echoDelay = null,
        TimeProvider? clock = null,
        CancellationToken ct = default)
    {
        var time = clock ?? TimeProvider.System;
        var probe = prompt[..Math.Min(EchoLength, prompt.Length)];
        var pause = echoDelay is { } given && given > TimeSpan.Zero ? given : echoDelay is null ? EchoDelay : Shortest;
        var before = Occurrences(await mux.GetTextAsync(pane, ct).ConfigureAwait(false), probe);
        var waited = TimeSpan.Zero;

        await mux.SendTextAsync(pane, prompt, ct).ConfigureAwait(false);

        for (var typing = 1; typing <= 2; typing++)
        {
            var sinceTyped = TimeSpan.Zero;

            do
            {
                await Task.Delay(pause, time, ct).ConfigureAwait(false);
                sinceTyped += pause;

                var shown = await mux.GetTextAsync(pane, ct).ConfigureAwait(false);

                if (Occurrences(shown, probe) > before)
                {
                    return PromptEcho.Echoed;
                }
            }
            while (waited + sinceTyped < within && sinceTyped < retry);

            waited += sinceTyped;

            if (waited >= within)
            {
                return PromptEcho.TimedOut;
            }

            if (typing == 2)
            {
                break;
            }

            await mux.SendTextAsync(pane, ClearLine, ct).ConfigureAwait(false);
            await mux.SendTextAsync(pane, prompt, ct).ConfigureAwait(false);
        }

        await mux.SendTextAsync(pane, ClearLine, ct).ConfigureAwait(false);

        return PromptEcho.Lost;
    }

    public static PromptDelivery DeliveryOf(PromptEcho echo) => echo switch
    {
        PromptEcho.Echoed => PromptDelivery.Echoed,
        PromptEcho.TimedOut => PromptDelivery.Deadline,
        _ => PromptDelivery.Lost,
    };

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
