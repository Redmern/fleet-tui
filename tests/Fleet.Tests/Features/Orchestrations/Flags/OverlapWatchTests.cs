using Fleet.Features.Orchestrations.Flags;
using Fleet.Platform.Git;
using Fleet.Ports.Agents.Models;
using Fleet.Shared.Constants;
using Fleet.Tests.EndToEnd.MixedTeam;

namespace Fleet.Tests.Features.Orchestrations.Flags;

// AC-19: live agents of one owner that changed the same files against their bases, against real git worktrees.
public sealed class OverlapWatchTests : IDisposable
{
    private const string Project = "techweb";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName(), "back\\slash");

    private readonly GitRunner _git = new();

    private readonly MixedTeamRig.MemoryStore _store = new();

    private readonly List<(AgentRecord About, string Line)> _told = [];

    private bool _ownerReachable = true;

    public OverlapWatchTests()
    {
        Directory.CreateDirectory(Repo);
        Directory.CreateDirectory(OwnerFolder);
    }

    private string Repo => Path.Combine(_root, "api");

    private string OwnerFolder => Path.Combine(_root, "orchestrations", "lead");

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_root)!, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private async Task GitAsync(string directory, params string[] args)
    {
        var result = await _git.RunAsync(directory, ["-c", "user.name=t", "-c", "user.email=t@t", .. args]);

        Assert.True(result.Ok, $"git {string.Join(' ', args)}: {result.Message}");
    }

    private async Task MainAsync()
    {
        await GitAsync(Repo, "init", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(Repo, "shared.cs"), "base");
        await GitAsync(Repo, "add", ".");
        await GitAsync(Repo, "commit", "-m", "base");
    }

    private async Task<AgentRecord> AgentAsync(string branch, string owner = "lead", bool open = true)
    {
        var worktree = Path.Combine(_root, branch.Replace('/', '-'));
        await GitAsync(Repo, "worktree", "add", "-b", branch, worktree, "main");
        var agent = new AgentRecord(worktree, "api", branch, AgentHarness.Claude, "main", false, Open: open, Owner: owner);
        _store.Save(Project, agent);

        return agent;
    }

    private async Task ChangeAsync(AgentRecord agent, params string[] files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(agent.Worktree, file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"{agent.Branch} {Guid.NewGuid()}");
        }

        await GitAsync(agent.Worktree, "add", ".");
        await GitAsync(agent.Worktree, "commit", "-m", $"change {string.Join(' ', files)}");
    }

    private OverlapWatch Watch() =>
        new(_store, _git, _ => OwnerFolder, (about, line, _) =>
        {
            _told.Add((about, line));
            return Task.FromResult(_ownerReachable);
        });

    [Fact]
    public async Task Two_live_agents_of_one_owner_that_changed_the_same_file_send_one_notice_naming_both_and_the_files()
    {
        await MainAsync();
        var a = await AgentAsync("feat/a");
        var b = await AgentAsync("feat/b");
        await ChangeAsync(a, "shared.cs", "src/only-a.cs");
        await ChangeAsync(b, "shared.cs", "src/only-b.cs");

        var sent = await Watch().CheckAsync(Project, "lead");

        Assert.Equal(1, sent);
        var (about, line) = Assert.Single(_told);
        Assert.Equal(a.Worktree, about.Worktree);
        Assert.StartsWith("api/feat/a and api/feat/b changed the same 1 file(s) against their bases: shared.cs.", line);
    }

    [Fact]
    public async Task The_same_shared_set_is_not_sent_again_even_by_a_new_watch_and_a_grown_set_is()
    {
        await MainAsync();
        var a = await AgentAsync("feat/a");
        var b = await AgentAsync("feat/b");
        await ChangeAsync(a, "shared.cs");
        await ChangeAsync(b, "shared.cs");

        await Watch().CheckAsync(Project, "lead");
        await ChangeAsync(a, "shared.cs");
        var again = await Watch().CheckAsync(Project, "lead");

        Assert.Equal(0, again);
        Assert.Single(_told);

        await ChangeAsync(a, "src/both.cs");
        await ChangeAsync(b, "src/both.cs");
        var grown = await Watch().CheckAsync(Project, "lead");

        Assert.Equal(1, grown);
        Assert.Contains("the same 2 file(s) against their bases: shared.cs, src/both.cs.", _told[^1].Line);
        Assert.True(File.Exists(OverlapLedger.PathIn(OwnerFolder)));
    }

    [Fact]
    public async Task Changes_only_in_the_worktree_are_not_compared()
    {
        await MainAsync();
        var a = await AgentAsync("feat/a");
        var b = await AgentAsync("feat/b");
        await ChangeAsync(a, "shared.cs");
        await File.WriteAllTextAsync(Path.Combine(b.Worktree, "shared.cs"), "not committed");

        Assert.Equal(0, await Watch().CheckAsync(Project, "lead"));
    }

    [Fact]
    public async Task Closed_agents_agents_of_another_owner_and_no_owner_are_left_out()
    {
        await MainAsync();
        var a = await AgentAsync("feat/a");
        var closed = await AgentAsync("feat/closed", open: false);
        var other = await AgentAsync("feat/other", owner: "someone-else");
        await ChangeAsync(a, "shared.cs");
        await ChangeAsync(closed, "shared.cs");
        await ChangeAsync(other, "shared.cs");

        Assert.Equal(0, await Watch().CheckAsync(Project, "lead"));
        Assert.Equal(0, await Watch().CheckAsync(Project, string.Empty));
        Assert.Empty(_told);
    }

    [Fact]
    public async Task An_agent_without_a_base_or_a_worktree_is_left_out()
    {
        await MainAsync();
        var a = await AgentAsync("feat/a");
        var b = await AgentAsync("feat/b");
        await ChangeAsync(a, "shared.cs");
        await ChangeAsync(b, "shared.cs");
        _store.Save(Project, b with { BaseRef = string.Empty });

        Assert.Equal(0, await Watch().CheckAsync(Project, "lead"));

        _store.Save(Project, b);
        Directory.Delete(b.Worktree, recursive: true);

        Assert.Equal(0, await Watch().CheckAsync(Project, "lead"));
    }

    [Fact]
    public async Task A_notice_that_could_not_be_delivered_is_not_remembered_and_is_tried_again()
    {
        await MainAsync();
        var a = await AgentAsync("feat/a");
        var b = await AgentAsync("feat/b");
        await ChangeAsync(a, "shared.cs");
        await ChangeAsync(b, "shared.cs");
        _ownerReachable = false;

        Assert.Equal(0, await Watch().CheckAsync(Project, "lead"));

        _ownerReachable = true;

        Assert.Equal(1, await Watch().CheckAsync(Project, "lead"));
        Assert.Equal(2, _told.Count);
    }

    [Fact]
    public async Task Two_checks_at_the_same_time_send_the_notice_once()
    {
        await MainAsync();
        var a = await AgentAsync("feat/a");
        var b = await AgentAsync("feat/b");
        await ChangeAsync(a, "shared.cs");
        await ChangeAsync(b, "shared.cs");
        var told = 0;

        OverlapWatch SlowWatch() =>
            new(_store, _git, _ => OwnerFolder, async (_, _, ct) =>
            {
                Interlocked.Increment(ref told);
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct);
                return true;
            });

        var sent = await Task.WhenAll(SlowWatch().CheckAsync(Project, "lead"), SlowWatch().CheckAsync(Project, "lead"));

        Assert.Equal(1, sent.Sum());
        Assert.Equal(1, told);
    }

    [Fact]
    public async Task The_ledger_keeps_each_file_of_a_pair_once()
    {
        await MainAsync();
        var a = await AgentAsync("feat/a");
        var b = await AgentAsync("feat/b");
        await ChangeAsync(a, "shared.cs");
        await ChangeAsync(b, "shared.cs");
        await Watch().CheckAsync(Project, "lead");
        await ChangeAsync(a, "more.cs");
        await ChangeAsync(b, "more.cs");
        await Watch().CheckAsync(Project, "lead");

        var lines = await File.ReadAllLinesAsync(OverlapLedger.PathIn(OwnerFolder));

        Assert.Equal(["api/feat/a|api/feat/b\tshared.cs", "api/feat/a|api/feat/b\tmore.cs"], lines);
    }
}
