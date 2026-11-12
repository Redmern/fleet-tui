using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;

namespace Fleet.Tests.Ports.Mux;

public class TypedPromptTests
{
    private const string Prompt = "Read AGENTS.md and TASK.md in your working directory, then begin.";

    private readonly FakeMuxDriver _mux = new();

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(5);

    private int Typings(PaneId pane) => _mux.SentTo(pane).Count(sent => sent == Prompt);

    private async Task UntilTypedAsync(PaneId pane, int times, Task typing)
    {
        while (Typings(pane) < times && !typing.IsCompleted)
        {
            await Task.Delay(Tick);
        }
    }

    // What a TUI that takes every key has in its input line: ctrl+u empties it, anything else is appended.
    private string InputLine(PaneId pane) =>
        _mux.SentTo(pane).Aggregate(string.Empty, (line, sent) => sent == TypedPrompt.ClearLine ? string.Empty : line + sent);

    [Fact]
    public async Task A_prompt_the_pane_shows_is_typed_once()
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        var typing = TypedPrompt.UntilEchoedAsync(_mux, pane, Prompt, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), Tick);

        await UntilTypedAsync(pane, 1, typing);
        _mux.SetText(pane, "┃  Read AGENTS.md and TASK.md in your working\n┃  directory, then begin.");

        Assert.True(await typing);
        Assert.Equal([Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_prompt_the_pane_dropped_is_typed_again_until_it_shows()
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        _mux.SetText(pane, "opencode is starting");
        var typing = TypedPrompt.UntilEchoedAsync(_mux, pane, Prompt, Tick, TimeSpan.FromSeconds(30), Tick);

        await UntilTypedAsync(pane, 3, typing);
        _mux.SetText(pane, "┃  " + Prompt);

        Assert.True(await typing);
        Assert.True(Typings(pane) >= 3);
        Assert.Equal(Prompt, _mux.SentTo(pane)[0]);
        Assert.All(
            _mux.SentTo(pane).Skip(1).Chunk(2),
            again => Assert.Equal([TypedPrompt.ClearLine, Prompt], again));
    }

    [Fact]
    public async Task A_slow_pane_that_took_every_typing_has_the_prompt_in_its_input_once()
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        var typing = TypedPrompt.UntilEchoedAsync(_mux, pane, Prompt, Tick, TimeSpan.FromSeconds(30), Tick);

        await UntilTypedAsync(pane, 3, typing);
        _mux.SetText(pane, "> " + InputLine(pane));

        Assert.True(await typing);
        Assert.Equal(Prompt, InputLine(pane));
    }

    [Fact]
    public async Task The_same_prompt_still_on_screen_from_an_earlier_message_is_not_taken_for_the_echo()
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        _mux.SetText(pane, $"user: {Prompt}\nopencode: done\n> ");
        var typing = TypedPrompt.UntilEchoedAsync(_mux, pane, Prompt, Tick, TimeSpan.FromSeconds(30), Tick);

        await UntilTypedAsync(pane, 2, typing);

        Assert.False(typing.IsCompleted);

        _mux.SetText(pane, $"user: {Prompt}\nopencode: done\n> {Prompt}");

        Assert.True(await typing);
    }

    [Fact]
    public async Task A_second_prompt_shown_next_to_the_earlier_one_is_typed_once()
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        _mux.SetText(pane, $"user: {Prompt}\n> ");
        var typing = TypedPrompt.UntilEchoedAsync(_mux, pane, Prompt, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), Tick);

        await UntilTypedAsync(pane, 1, typing);
        _mux.SetText(pane, $"user: {Prompt}\n> {Prompt}");

        Assert.True(await typing);
        Assert.Equal([Prompt], _mux.SentTo(pane));
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
