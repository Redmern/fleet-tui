using System.Reflection;
using System.Text.RegularExpressions;
using Fleet.Cli.Composition;
using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.AddRepository.Models;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Platform.Git;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ports.Notifications.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Ui;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;

namespace Fleet.Tests.Cli;

public sealed partial class DashboardSkeletonTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fleet-tests", $"skeleton-{Guid.NewGuid():N}");

    public DashboardSkeletonTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AgentRecord Agent(string branch, string owner = "", bool hidden = false, string status = "") =>
        new($@"C:\wt\{branch}", "api", branch, AgentHarness.Claude, "origin/main", true,
            Hidden: hidden, Owner: owner, Status: status);

    private static AgentRecord Sub(string branch, string status = "") =>
        new($@"C:\subs\{branch}", "", branch, AgentHarness.Orchestrator, "", false, Status: status);

    private static string Text(IReadOnlyList<FleetSpan> spans) => string.Concat(spans.Select(s => s.Text));

    private static bool StartsWithUnknownPill(FleetRow row, string branch)
    {
        var pill = BranchStatus.Pill(branch, BranchState.Unknown);
        return row.Spans.Take(pill.Count).SequenceEqual(pill);
    }

    [Fact]
    public void Fast_agent_rows_show_unknown_pills_and_keep_hidden_and_status_from_the_store()
    {
        var stored = new[]
        {
            Agent("feat-a", status: AgentActivity.Working),
            Agent("feat-b", hidden: true),
            Agent("child", owner: "sub-1"),
            Sub("sub-1"),
        };

        var board = DashboardSkeleton.Agents(stored);

        Assert.Equal(2, board.Count);
        Assert.True(StartsWithUnknownPill(board.Rows[0], "feat-a"));
        Assert.True(StartsWithUnknownPill(board.Rows[1], "feat-b"));
        Assert.Equal([false, true], board.Hidden);
        Assert.Equal(AgentActivity.Working, board.StatusAt(0));
        Assert.Contains(FleetGlyphs.Hidden, Text(board.Rows[1].Trailing ?? []), StringComparison.Ordinal);
    }

    [Fact]
    public void Fast_sub_rows_show_unknown_pills_for_children_in_the_tree_order()
    {
        var stored = new[]
        {
            Agent("zeta", owner: "sub-1"),
            Sub("sub-1", status: "done"),
            Agent("alpha", owner: "sub-1", hidden: true),
        };

        var subs = DashboardSkeleton.Subs(stored, "/fleet");

        Assert.Equal(1, subs.Count);
        Assert.Equal(3, subs.Rows.Count);
        Assert.Contains("sub-1", Text(subs.Rows[0].Spans), StringComparison.Ordinal);
        Assert.Contains(Text(BranchStatus.Pill("alpha", BranchState.Unknown)), Text(subs.Rows[1].Spans), StringComparison.Ordinal);
        Assert.Contains(Text(BranchStatus.Pill("zeta", BranchState.Unknown)), Text(subs.Rows[2].Spans), StringComparison.Ordinal);
        Assert.Equal([false, true, false], subs.Hidden);
    }

    [Fact]
    public void Fast_notices_render_what_the_store_holds()
    {
        var now = DateTime.UtcNow;
        var stored = new[]
        {
            new Notice("p", NoticeKind.Done, @"C:\wt\a", "a", "finished", now),
            new Notice("p", NoticeKind.Failed, @"C:\wt\b", "b", "broke", now, Dismissed: now),
        };

        var board = DashboardSkeleton.Notices(stored, now);

        Assert.Equal(2, board.Rows.Count);
        Assert.Equal(1, board.Open);
        Assert.Equal(stored[0].Key, board.KeyAt(0));
    }

    [Fact]
    public async Task Fast_repositories_match_what_git_lists_without_running_git()
    {
        var add = new AddRepositoryHandler(new GitRunner());
        await add.HandleAsync(AddRepositoryCommand.CreateNew(_root, "zeta", "main"));
        await add.HandleAsync(AddRepositoryCommand.CreateNew(_root, "alpha", "develop"));
        Directory.CreateDirectory(Path.Combine(_root, "not-a-repo"));
        Directory.CreateDirectory(Path.Combine(_root, ".fleet"));

        var slow = await new ListRepositoriesHandler(new GitRunner()).HandleAsync(_root);

        Assert.Equal(slow, RepositoryFolders.Skim(_root));
        Assert.Equal(["alpha", "zeta"], DashboardSkeleton.Repositories(_root).Select(r => r.Name));
        Assert.Equal("develop", DashboardSkeleton.Repositories(_root)[0].DefaultBranch);
    }

    [Fact]
    public void A_folder_that_is_itself_a_bare_repository_is_found_from_its_files()
    {
        var bare = Directory.CreateDirectory(Path.Combine(_root, "plain.git")).FullName;
        Directory.CreateDirectory(Path.Combine(bare, "objects"));
        Directory.CreateDirectory(Path.Combine(bare, "refs"));
        File.WriteAllText(Path.Combine(bare, "HEAD"), "ref: refs/heads/trunk\n");
        File.WriteAllText(Path.Combine(bare, "config"), "[core]\n\tbare = true\n");

        var checkout = Directory.CreateDirectory(Path.Combine(_root, "checkout", ".git")).FullName;
        Directory.CreateDirectory(Path.Combine(checkout, "objects"));
        Directory.CreateDirectory(Path.Combine(checkout, "refs"));
        File.WriteAllText(Path.Combine(checkout, "HEAD"), "ref: refs/heads/main\n");
        File.WriteAllText(Path.Combine(checkout, "config"), "[core]\n\tbare = false\n");

        var found = Assert.Single(RepositoryFolders.Skim(_root));

        Assert.Equal("plain.git", found.Name);
        Assert.Equal("trunk", found.DefaultBranch);
    }

    [Fact]
    public void A_missing_project_root_has_no_repositories() =>
        Assert.Empty(RepositoryFolders.Skim(Path.Combine(_root, "nope")));

    [GeneratedRegex(@"^ {12}(\w+Fast):\s(.*?)(?=^ {12}[A-Z]\w*:\s|\z)", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex FastCallback();

    [GeneratedRegex(@"\b(mux|git|states|WithBarState|WithActivity|repositories|LoadNotices|CloseIdle|AgainstBase)\b")]
    private static partial Regex SlowWork();

    [Fact]
    public void The_fast_loaders_never_reach_the_mux_or_git()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot, "src", "Fleet", "Cli", "Composition", "DashboardWiring.cs"));
        var fast = FastCallback().Matches(source).ToList();

        Assert.Equal(
            ["LoadRepositoriesFast", "LoadAgentsFast", "LoadSubsFast", "LoadNoticesFast"],
            fast.Select(m => m.Groups[1].Value));

        Assert.All(fast, m => Assert.False(
            SlowWork().IsMatch(m.Groups[2].Value),
            $"{m.Groups[1].Value} does slow work: {SlowWork().Match(m.Groups[2].Value).Value}"));

        var skeleton = typeof(DashboardSkeleton).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SelectMany(method => method.GetParameters())
            .Select(p => p.ParameterType.Name);

        Assert.DoesNotContain("IMuxDriver", skeleton);
        Assert.DoesNotContain("IGitRunner", skeleton);
    }

    private static string RepoRoot { get; } =
        typeof(DashboardSkeletonTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value!;
}
