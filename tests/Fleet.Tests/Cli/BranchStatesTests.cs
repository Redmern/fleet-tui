using System.Collections.Concurrent;
using Fleet.Cli.Composition;
using Fleet.Ports.Git;
using Fleet.Ports.Git.Models;
using Fleet.Shared;

namespace Fleet.Tests.Cli;

public sealed class BranchStatesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"fleet-branchstates-{Guid.NewGuid():N}");

    public BranchStatesTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Worktree(string name) => Directory.CreateDirectory(Path.Combine(_root, name)).FullName;

    [Fact]
    public void A_worktree_asked_for_twice_in_one_refresh_runs_git_once()
    {
        var git = new CountingGit();
        var states = new BranchStates(git);
        var tree = Worktree("api");

        var first = states.For(tree, "origin/main");
        var second = states.For(tree, "origin/main");

        Assert.Equal(first, second);
        Assert.Equal(1, git.StatusCalls(tree));
        Assert.Equal((2, 1, true), (first.Ahead, first.Behind, first.Dirty));
    }

    [Fact]
    public void A_state_older_than_its_freshness_is_measured_again()
    {
        var git = new CountingGit();
        var states = new BranchStates(git, TimeSpan.Zero);
        var tree = Worktree("api");

        states.For(tree);
        states.For(tree);

        Assert.Equal(2, git.StatusCalls(tree));
    }

    [Fact]
    public void Warming_measures_every_worktree_side_by_side_and_each_only_once()
    {
        var git = new CountingGit { Delay = TimeSpan.FromMilliseconds(150) };
        var states = new BranchStates(git);
        var trees = Enumerable.Range(0, 6).Select(i => Worktree($"wt{i}")).ToList();

        states.Warm([.. trees.Select(t => (t, (string?)null)), (trees[0], (string?)null)]);

        // Side by side is shown by overlap, not by wall-clock time, which a busy CI runner stretches.
        Assert.All(trees, t => Assert.Equal(1, git.StatusCalls(t)));
        Assert.True(git.MostAtOnce > 1, $"at most {git.MostAtOnce} git at a time");

        foreach (var tree in trees)
        {
            states.For(tree);
        }

        Assert.All(trees, t => Assert.Equal(1, git.StatusCalls(t)));
    }

    [Fact]
    public void Peeking_returns_unknown_at_once_and_the_measured_state_once_git_has_answered()
    {
        var git = new CountingGit { Gate = new TaskCompletionSource() };
        var states = new BranchStates(git);
        var tree = Worktree("api");

        Assert.Equal(BranchState.Unknown, states.Peek(tree, "origin/main"));
        Assert.Equal(BranchState.Unknown, states.Peek(tree, "origin/main"));

        git.Gate.SetResult();

        Assert.True(SpinWait.SpinUntil(
            () => states.Peek(tree, "origin/main") != BranchState.Unknown, TimeSpan.FromSeconds(10)));
        Assert.Equal((2, 1, true), (states.Peek(tree, "origin/main").Ahead, states.Peek(tree, "origin/main").Behind, states.Peek(tree, "origin/main").Dirty));
        Assert.Equal(1, git.StatusCalls(tree));
    }

    [Fact]
    public void A_stale_state_is_still_shown_while_it_is_measured_again()
    {
        var git = new CountingGit();
        var states = new BranchStates(git, TimeSpan.Zero);
        var tree = Worktree("api");
        var measured = states.For(tree);

        git.Gate = new TaskCompletionSource();

        Assert.Equal(measured, states.Peek(tree));
        Assert.Equal(measured, states.Peek(tree));

        git.Gate.SetResult();

        Assert.True(SpinWait.SpinUntil(() => git.StatusCalls(tree) == 2, TimeSpan.FromSeconds(10)));
        Assert.False(SpinWait.SpinUntil(() => git.StatusCalls(tree) > 2, TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void Peeking_many_worktrees_runs_a_bounded_number_of_git_at_a_time()
    {
        var git = new CountingGit { Delay = TimeSpan.FromMilliseconds(100) };
        var states = new BranchStates(git);
        var trees = Enumerable.Range(0, 12).Select(i => Worktree($"wt{i}")).ToList();

        foreach (var tree in trees)
        {
            Assert.Equal(BranchState.Unknown, states.Peek(tree));
        }

        Assert.True(SpinWait.SpinUntil(
            () => trees.All(t => git.StatusCalls(t) == 1), TimeSpan.FromSeconds(20)));
        Assert.InRange(git.MostAtOnce, 1, 6);
    }

    private sealed class CountingGit : IGitRunner
    {
        private readonly ConcurrentDictionary<string, int> _status = new();
        private int _running;

        public TimeSpan Delay { get; init; }

        public TaskCompletionSource? Gate { get; set; }

        public int MostAtOnce { get; private set; }

        public int StatusCalls(string worktree) => _status.GetValueOrDefault(worktree);

        public async Task<GitResult> RunAsync(string workDir, IReadOnlyList<string> args, string? stdin = null, CancellationToken ct = default)
        {
            var now = Interlocked.Increment(ref _running);
            lock (_status)
            {
                MostAtOnce = Math.Max(MostAtOnce, now);
            }

            try
            {
                if (Gate is { } gate)
                {
                    await gate.Task.WaitAsync(ct);
                }

                if (Delay > TimeSpan.Zero)
                {
                    await Task.Delay(Delay, ct);
                }

                if (args[0] == "status")
                {
                    _status.AddOrUpdate(workDir, 1, (_, n) => n + 1);
                    return new GitResult(0, " M file.cs\n", string.Empty);
                }

                return new GitResult(0, "1\t2\n", string.Empty);
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }
}
