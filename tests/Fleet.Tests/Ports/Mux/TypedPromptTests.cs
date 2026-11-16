using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Models;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.Ports.Mux;

public class TypedPromptTests
{
    private const string Prompt = "Read AGENTS.md and TASK.md in your working directory, then begin.";

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(5);

    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(20);

    private static readonly TimeSpan Within = TimeSpan.FromSeconds(30);

    private readonly FakeMuxDriver _mux = new();

    private readonly SkippingClock _clock = new();

    private async Task<PaneId> PaneAsync(string? text = null)
    {
        var pane = await _mux.SpawnAsync(new SpawnOptions { NewWindow = true });

        if (text is not null)
        {
            _mux.SetText(pane, text);
        }

        return pane;
    }

    private Task<PromptEcho> TypeAsync(PaneId pane, TimeSpan? within = null) =>
        TypedPrompt.UntilEchoedAsync(_mux, pane, Prompt, Retry, within ?? Within, Tick, _clock);

    private void OnPoll(Action<int> script)
    {
        var polls = 0;
        _clock.Skipped += () => script(++polls);
    }

    [Fact]
    public async Task A_prompt_the_pane_shows_is_typed_once()
    {
        var pane = await PaneAsync();
        OnPoll(_ => _mux.SetText(pane, "┃  Read AGENTS.md and TASK.md in your working\n┃  directory, then begin."));

        Assert.Equal(PromptEcho.Echoed, await TypeAsync(pane));
        Assert.Equal([Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_prompt_the_pane_dropped_is_typed_again_once_after_clearing_the_line()
    {
        var pane = await PaneAsync("opencode is starting");
        OnPoll(poll =>
        {
            if (_mux.SentTo(pane).Count(s => s == Prompt) == 2)
            {
                _mux.SetText(pane, "┃  " + Prompt);
            }
        });

        Assert.Equal(PromptEcho.Echoed, await TypeAsync(pane));
        Assert.Equal([Prompt, TypedPrompt.ClearLine, Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_pane_that_never_shows_the_prompt_is_typed_into_twice_at_most()
    {
        var pane = await PaneAsync("opencode is starting");

        Assert.Equal(PromptEcho.Lost, await TypeAsync(pane));
        Assert.Equal([Prompt, TypedPrompt.ClearLine, Prompt, TypedPrompt.ClearLine], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_blank_pane_is_a_pane_that_has_not_shown_the_prompt_yet_so_it_is_retyped_once()
    {
        var pane = await PaneAsync();

        Assert.Equal(PromptEcho.Lost, await TypeAsync(pane));
        Assert.Equal([Prompt, TypedPrompt.ClearLine, Prompt, TypedPrompt.ClearLine], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_prompt_that_shows_during_the_second_wait_is_not_typed_a_third_time()
    {
        var pane = await PaneAsync("opencode is starting");
        OnPoll(poll =>
        {
            if (poll == 6)
            {
                _mux.SetText(pane, "┃  " + Prompt);
            }
        });

        Assert.Equal(PromptEcho.Echoed, await TypeAsync(pane));
        Assert.Equal([Prompt, TypedPrompt.ClearLine, Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_prompt_that_shows_late_in_the_first_wait_is_not_typed_again()
    {
        var pane = await PaneAsync("opencode is starting");
        OnPoll(poll =>
        {
            if (poll == 3)
            {
                _mux.SetText(pane, "┃  " + Prompt);
            }
        });

        Assert.Equal(PromptEcho.Echoed, await TypeAsync(pane));
        Assert.Equal([Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task The_same_prompt_on_screen_from_an_earlier_message_is_not_taken_for_the_echo_and_a_dropped_typing_is_retyped_once()
    {
        var pane = await PaneAsync($"user: {Prompt}\nopencode: done\n> ");

        Assert.Equal(PromptEcho.Lost, await TypeAsync(pane));
        Assert.Equal([Prompt, TypedPrompt.ClearLine, Prompt, TypedPrompt.ClearLine], _mux.SentTo(pane));
    }

    [Fact]
    public async Task The_same_prompt_on_screen_from_an_earlier_message_and_a_retype_that_shows_is_submitted_once()
    {
        var pane = await PaneAsync($"user: {Prompt}\nopencode: done\n> ");
        OnPoll(_ =>
        {
            if (_mux.SentTo(pane).Count(s => s == Prompt) == 2)
            {
                _mux.SetText(pane, $"user: {Prompt}\nopencode: done\n> {Prompt}");
            }
        });

        Assert.Equal(PromptEcho.Echoed, await TypeAsync(pane));
        Assert.Equal([Prompt, TypedPrompt.ClearLine, Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task A_second_prompt_shown_next_to_the_earlier_one_is_typed_once()
    {
        var pane = await PaneAsync($"user: {Prompt}\n> ");
        OnPoll(_ => _mux.SetText(pane, $"user: {Prompt}\n> {Prompt}"));

        Assert.Equal(PromptEcho.Echoed, await TypeAsync(pane));
        Assert.Equal([Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task No_time_to_wait_types_the_prompt_once_leaves_it_in_the_line_and_does_not_retry()
    {
        var pane = await PaneAsync("opencode is starting");

        Assert.Equal(PromptEcho.TimedOut, await TypeAsync(pane, TimeSpan.Zero));
        Assert.Equal([Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task Time_that_runs_out_during_the_second_wait_does_not_clear_the_line()
    {
        var pane = await PaneAsync("opencode is starting");

        Assert.Equal(PromptEcho.TimedOut, await TypeAsync(pane, TimeSpan.FromMilliseconds(30)));
        Assert.Equal([Prompt, TypedPrompt.ClearLine, Prompt], _mux.SentTo(pane));
    }

    [Fact]
    public async Task Every_wait_runs_on_the_given_clock()
    {
        var pane = await PaneAsync("opencode is starting");

        await TypeAsync(pane);

        Assert.NotEmpty(_clock.Delays);
        Assert.All(_clock.Delays, delay => Assert.Equal(Tick, delay));
        Assert.True(_clock.Delays.Count <= 10);
    }
}
