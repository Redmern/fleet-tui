using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Ports.Mux;

public class TypedPromptTests
{
    private const string Prompt = "Read AGENTS.md and TASK.md in your working directory, then begin.";

    private readonly FakeMuxDriver _mux = new();

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(5);

    [Fact]
    public async Task A_prompt_the_pane_shows_is_typed_once()
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        _mux.SetText(pane, "┃  Read AGENTS.md and TASK.md in your working\n┃  directory, then begin.");

        var echoed = await TypedPrompt.UntilEchoedAsync(_mux, pane, Prompt, Tick, TimeSpan.FromSeconds(5), Tick);

        Assert.True(echoed);
        Assert.Equal([Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_prompt_the_pane_dropped_is_typed_again_until_it_shows()
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        _mux.SetText(pane, "opencode is starting");
        var typing = TypedPrompt.UntilEchoedAsync(_mux, pane, Prompt, Tick, TimeSpan.FromSeconds(30), Tick);

        while (_mux.SentTo(pane).Count < 3 && !typing.IsCompleted)
        {
            await Task.Delay(Tick);
        }

        _mux.SetText(pane, "┃  " + Prompt);

        Assert.True(await typing);
        Assert.True(_mux.SentTo(pane).Count >= 3);
        Assert.All(_mux.SentTo(pane), sent => Assert.Equal(Prompt, sent));
    }

    [Fact]
    public async Task A_prompt_that_shows_late_is_not_typed_a_second_time()
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        var typing = TypedPrompt.UntilEchoedAsync(
            _mux, pane, Prompt, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), Tick);

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        _mux.SetText(pane, "┃  " + Prompt);

        Assert.True(await typing);
        Assert.Equal([Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_pane_that_never_shows_the_prompt_stops_being_typed_into_after_the_limit()
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });

        var echoed = await TypedPrompt.UntilEchoedAsync(_mux, pane, Prompt, Tick, TimeSpan.Zero, Tick);

        Assert.False(echoed);
        Assert.Equal([Prompt], _mux.SentTo(pane));
    }
}
