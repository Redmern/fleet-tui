using Fleet.Features.Agents.NewAgent;
using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Ui;
using Terminal.Gui.App;
using Terminal.Gui.Input;

namespace Fleet.Tests.Features.Agents;

// Drives the real New agent card on the ansi test driver, so the repository
// and base-branch pickers it opens take the typed keys the way a terminal
// would deliver them.
[Collection(ModalDepthCollection.Name)]
public sealed class NewAgentDialogTests
{
    private static readonly NewAgentPrompt Prompt = new(
        "demo",
        [("fleet", "/r/fleet", "main"), ("site", "/r/site", "main"), ("sitemap", "/r/sitemap", "main")],
        0,
        _ => [new BranchChoice("main", false), new BranchChoice("feat/search", false), new BranchChoice("origin/feat/remote", true)],
        "claude");

    private sealed record Outcome(NewAgentCommand? Command, IReadOnlyList<string> PickerRows);

    private static Outcome Run(Key open, string typed)
    {
        using var app = Application.Create().Init("ansi");
        IReadOnlyList<string> rows = [];

        void Press(Key key) => app.Keyboard.RaiseKeyDownEvent(key);

        Step(app,
        [
            () => Press(open),
            .. typed.Select(c => (Action)(() => Press(new Key(c)))),
            () => rows = [.. app.TopRunnableView!.SubViews.OfType<FleetList>().Single().Source!.ToList().Cast<string>()],
            () => Press(Key.Enter),
            () => Press(Key.Enter.WithCtrl),
        ], 0);

        return new Outcome(NewAgentView.Show(app, Prompt, Keymap.Default), rows);
    }

    // Each step queues the next before it runs, since a key that opens a
    // picker runs a nested loop. A step waits while the dialog on top is
    // still closing, so it reaches the dialog underneath.
    private static void Step(IApplication app, IReadOnlyList<Action> steps, int at) =>
        app.AddTimeout(TimeSpan.FromMilliseconds(1), () =>
        {
            if (app.TopRunnable is { StopRequested: true })
            {
                return true;
            }

            if (at < steps.Count - 1)
            {
                Step(app, steps, at + 1);
            }

            steps[at]();
            return false;
        });

    [Fact]
    public void Typing_in_the_repository_picker_filters_it_and_enter_takes_the_match()
    {
        var outcome = Run(new Key('r'), "map");

        Assert.Single(outcome.PickerRows);
        Assert.Contains("sitemap", outcome.PickerRows[0]);
        Assert.Equal("sitemap", outcome.Command?.RepositoryName);
    }

    [Fact]
    public void Typing_in_the_base_branch_picker_filters_it_and_enter_takes_the_match()
    {
        var outcome = Run(new Key('a'), "remote");

        Assert.Single(outcome.PickerRows);
        Assert.Contains("origin/feat/remote", outcome.PickerRows[0]);
        Assert.Equal("origin/feat/remote", outcome.Command?.Base);
    }
}
