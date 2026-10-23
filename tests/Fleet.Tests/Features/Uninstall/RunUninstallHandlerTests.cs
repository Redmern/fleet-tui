using Fleet.Features.Uninstall.RunUninstall;
using Fleet.Features.Uninstall.RunUninstall.Models;
using Fleet.Ports.Uninstall;
using Fleet.Ports.Uninstall.Enums;
using Fleet.Ports.Uninstall.Models;

namespace Fleet.Tests.Features.Uninstall;

public class RunUninstallHandlerTests
{
    private sealed class FakeTargets(params UninstallItem[] items) : IUninstallTargets
    {
        public List<UninstallItem> Removed { get; } = [];

        public Dictionary<string, Exception> Failing { get; } = [];

        public IReadOnlyList<UninstallItem> Discover() => items;

        public void Remove(UninstallItem item)
        {
            if (Failing.TryGetValue(item.Path, out var failure))
            {
                throw failure;
            }

            Removed.Add(item);
        }
    }

    private static readonly UninstallItem Binary = new(UninstallKind.Binary, "/usr/local/bin/fleet", "the fleet binary");

    private static readonly UninstallItem Other = new(UninstallKind.Directory, "/home/me/.config/fleet", "fleet config");

    private static (int Exit, List<string> Output, int Asked) Run(
        FakeTargets targets, bool yes = false, bool interactive = true, bool answer = false)
    {
        var output = new List<string>();
        var asked = 0;

        var exit = new RunUninstallHandler(targets).Handle(
            new RunUninstallCommand(yes, interactive),
            () =>
            {
                asked++;
                return answer;
            },
            output.Add);

        return (exit, output, asked);
    }

    [Fact]
    public void The_plan_lists_every_item_with_the_binary_last()
    {
        var plan = new RunUninstallHandler(new FakeTargets(Binary, Other)).Plan();

        Assert.Equal([Other, Binary], plan.Items);
    }

    [Fact]
    public void The_plan_lines_name_each_item_and_its_path()
    {
        var plan = new UninstallPlan([Other, Binary]);

        var lines = RunUninstallHandler.PlanLines(plan);

        Assert.Contains(lines, l => l.Contains("fleet config", StringComparison.Ordinal) && l.Contains(Other.Path, StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("the fleet binary", StringComparison.Ordinal) && l.Contains(Binary.Path, StringComparison.Ordinal));
    }

    [Fact]
    public void It_prints_the_plan_and_asks_once_before_removing_anything()
    {
        var targets = new FakeTargets(Binary, Other);
        var output = new List<string>();
        var removedWhenAsked = -1;
        var asked = 0;

        new RunUninstallHandler(targets).Handle(
            new RunUninstallCommand(false, true),
            () =>
            {
                asked++;
                removedWhenAsked = targets.Removed.Count;
                Assert.Contains(output, l => l.Contains(Binary.Path, StringComparison.Ordinal));
                Assert.Contains(output, l => l.Contains(Other.Path, StringComparison.Ordinal));
                return true;
            },
            output.Add);

        Assert.Equal(1, asked);
        Assert.Equal(0, removedWhenAsked);
    }

    [Fact]
    public void Answering_yes_removes_every_item_with_the_binary_last()
    {
        var targets = new FakeTargets(Binary, Other);

        var (exit, _, _) = Run(targets, answer: true);

        Assert.Equal(0, exit);
        Assert.Equal([Other, Binary], targets.Removed);
    }

    [Fact]
    public void Answering_anything_but_yes_removes_nothing_and_exits_0()
    {
        var targets = new FakeTargets(Binary, Other);

        var (exit, output, _) = Run(targets, answer: false);

        Assert.Equal(0, exit);
        Assert.Empty(targets.Removed);
        Assert.Contains(output, l => l.Contains("nothing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Yes_skips_the_confirmation_and_removes_everything()
    {
        var targets = new FakeTargets(Binary, Other);

        var (exit, _, asked) = Run(targets, yes: true, interactive: false);

        Assert.Equal(0, exit);
        Assert.Equal(0, asked);
        Assert.Equal([Other, Binary], targets.Removed);
    }

    [Fact]
    public void Without_a_tty_and_without_yes_it_exits_1_without_asking()
    {
        var targets = new FakeTargets(Binary);

        var (exit, output, asked) = Run(targets, interactive: false, answer: true);

        Assert.Equal(1, exit);
        Assert.Equal(0, asked);
        Assert.Empty(targets.Removed);
        Assert.Equal([RunUninstallHandler.NoTtyRefusal], output);
    }

    [Fact]
    public void A_failed_removal_is_reported_and_the_rest_still_go_and_it_exits_1()
    {
        var targets = new FakeTargets(Binary, Other);
        targets.Failing.Add(Other.Path, new IOException("in use"));

        var (exit, output, _) = Run(targets, yes: true);

        Assert.Equal(1, exit);
        Assert.Equal([Binary], targets.Removed);
        Assert.Contains(output, l => l.Contains(Other.Path, StringComparison.Ordinal) && l.Contains("in use", StringComparison.Ordinal));
    }

    [Fact]
    public void An_empty_plan_removes_nothing_and_does_not_ask()
    {
        var (exit, _, asked) = Run(new FakeTargets());

        Assert.Equal(0, exit);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Any_failure_is_reported_and_the_rest_still_go_and_it_exits_1()
    {
        var targets = new FakeTargets(Binary, Other);
        targets.Failing.Add(Other.Path, new InvalidOperationException("odd state"));

        var (exit, output, _) = Run(targets, yes: true);

        Assert.Equal(1, exit);
        Assert.Equal([Binary], targets.Removed);
        Assert.Contains(output, l => l.Contains(Other.Path, StringComparison.Ordinal) && l.Contains("odd state", StringComparison.Ordinal));
    }

    [Fact]
    public void A_successful_uninstall_notes_what_remains()
    {
        var (exit, output, _) = Run(new FakeTargets(Binary, Other), yes: true);

        Assert.Equal(0, exit);
        var note = string.Join('\n', output.SkipWhile(l => !l.Contains("done", StringComparison.Ordinal)));
        Assert.Contains("worktrees", note, StringComparison.Ordinal);
        Assert.Contains(".fleet", note, StringComparison.Ordinal);
        Assert.Contains("~/.claude", note, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failed_uninstall_does_not_claim_success()
    {
        var targets = new FakeTargets(Binary, Other);
        targets.Failing.Add(Other.Path, new IOException("in use"));

        var (_, output, _) = Run(targets, yes: true);

        Assert.DoesNotContain(output, l => l.Contains("worktrees", StringComparison.Ordinal));
    }
}
