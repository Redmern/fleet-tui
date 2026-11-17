using System.Text;
using Fleet.Features.Repositories.OpenPr;
using Fleet.Features.Repositories.OpenPr.Models;
using Fleet.Platform.Git;
using Fleet.Ports.Forge.Enums;
using Fleet.Ports.Forge.Models;
using Fleet.Ports.Git.Models;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Repositories.OpenPr;

// AC-22, 23, 25 against real git: a bare "forge" repository that takes push options and answers an AGit push the
// way Forgejo does (a post-receive hook prints the pull request line), a container with origin and a github-like
// mirror whose push URL is DISABLED, exactly as this repository has it.
public sealed class OpenPrHandlerTests : IDisposable
{
    private const string Url = "https://git.example.test/acme/api/pulls/71";

    // The folder name has a backslash on Linux, as CLAUDE.md asks of temp folders that end up in arguments.
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "fleet-open-pr-" + Guid.NewGuid().ToString("N"), OperatingSystem.IsWindows() ? "w" : @"w\x");

    private readonly GitRunner _git = new();
    private readonly FakeProcessRunner _processes = new();
    private readonly FakeForgeProbe _probe = new();

    public OpenPrHandlerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path.GetDirectoryName(_root)!, true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string Forge => Path.Combine(_root, "forge.git");

    private string Other => Path.Combine(_root, "other.git");

    private string Container => Path.Combine(_root, "api");

    private string BodyFile => Path.Combine(_root, "pr-body.md");

    private OpenPrHandler Handler => new(_git, _processes, _probe);

    private OpenPrCommand Command(
        string? forge = "forgejo", string? pushRemote = null, string branch = "feat/x", string? bodyFile = null) =>
        new("api", Container, branch, "main", "feat: open pull requests", bodyFile ?? BodyFile,
            RepositorySettings.From(forge, pushRemote), _root);

    private async Task<string> RunAsync(string dir, params string[] args)
    {
        var result = await _git.RunAsync(dir, ["-c", "user.name=t", "-c", "user.email=t@t", .. args]);

        Assert.True(result.Ok, $"git {string.Join(' ', args)}: {result.Message}");

        return result.Out;
    }

    private async Task<string> ForgeRepositoryAsync(string path, bool answers = true)
    {
        await RunAsync(_root, "init", "--bare", "-b", "main", path);
        await RunAsync(path, "config", "receive.advertisePushOptions", "true");

        if (answers)
        {
            var hook = Path.Combine(path, "hooks", "post-receive");

            Directory.CreateDirectory(Path.GetDirectoryName(hook)!);
            File.WriteAllText(
                hook,
                "#!/bin/sh\n"
                + "i=0\n"
                + "while [ \"$i\" -lt \"${GIT_PUSH_OPTION_COUNT:-0}\" ]; do\n"
                + "  eval \"printf '%s\\\\n' \\\"\\$GIT_PUSH_OPTION_$i\\\"\" >> push-options.txt\n"
                + "  i=$((i+1))\n"
                + "done\n"
                + "while read old new ref; do\n"
                + "  echo \"$ref\" >> refs-pushed.txt\n"
                + "  case \"$ref\" in refs/for/*)\n"
                + "    echo \"\"; echo \"Visit the existing pull request:\"; echo \"  " + Url + "\"; echo \"\";;\n"
                + "  esac\n"
                + "done\n");

            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    hook,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }

        return path;
    }

    private async Task LayoutAsync(bool answers = true)
    {
        var seed = Path.Combine(_root, "seed");

        await ForgeRepositoryAsync(Forge, answers);
        await RunAsync(_root, "clone", Forge, seed);
        await RunAsync(seed, "switch", "-c", "main");
        await RunAsync(seed, "commit", "--allow-empty", "-m", "first");
        await RunAsync(seed, "push", "origin", "HEAD:main");

        await RunAsync(_root, "init", "--bare", "-b", "main", Container);
        await RunAsync(Container, "remote", "add", "origin", Forge);
        await RunAsync(Container, "config", "remote.origin.fetch", "+refs/heads/*:refs/remotes/origin/*");
        await RunAsync(Container, "fetch", "origin");
        await RunAsync(Container, "branch", "main", "origin/main");
        await RunAsync(Container, "branch", "feat/x", "main");

        // the mirror of this repository: fetch from GitHub, push URL DISABLED
        await RunAsync(Container, "remote", "add", "github", "https://github.com/acme/api");
        await RunAsync(Container, "remote", "set-url", "--push", "github", "DISABLED");

        File.WriteAllText(BodyFile, "## Summary\nOpens pull requests.\n\n## Merge Danger\n- Door: two-way\n");

        // the seed's push of main is not one of fleet's
        File.Delete(Path.Combine(Forge, "refs-pushed.txt"));
    }

    private string[] Lines(string repository, string file) =>
        File.Exists(Path.Combine(repository, file)) ? File.ReadAllLines(Path.Combine(repository, file)) : [];

    private async Task<bool> HasRefAsync(string repository, string reference) =>
        (await _git.RunAsync(repository, ["rev-parse", "--verify", "--quiet", reference])).Ok;

    [Fact]
    public async Task A_forgejo_repository_gets_the_branch_pushed_then_an_agit_push_and_the_url_the_forge_printed()
    {
        await LayoutAsync();

        var opened = await Handler.HandleAsync(Command());

        Assert.True(opened.Succeeded, opened.Error);
        Assert.StartsWith(Url, opened.Value);
        Assert.Contains("did not merge", opened.Value);

        Assert.Equal(["refs/heads/feat/x", "refs/for/main"], Lines(Forge, "refs-pushed.txt"));

        var options = Lines(Forge, "push-options.txt");

        Assert.Equal("topic=feat/x", options[0]);
        Assert.Equal("title=feat: open pull requests", options[1]);
        Assert.StartsWith("description={base64}", options[2]);
        Assert.Equal(
            File.ReadAllText(BodyFile).TrimEnd('\n'),
            Encoding.UTF8.GetString(Convert.FromBase64String(options[2]["description={base64}".Length..])));

        Assert.Equal(
            await RunAsync(Container, "rev-parse", "refs/heads/feat/x"),
            await RunAsync(Forge, "rev-parse", "refs/heads/feat/x"));
        Assert.Equal("origin", await RunAsync(Container, "config", "branch.feat/x.remote"));
        Assert.Equal(await RunAsync(Container, "rev-parse", "main"), await RunAsync(Forge, "rev-parse", "main"));
        Assert.Empty(_processes.Runs);
    }

    [Fact]
    public async Task A_push_that_prints_no_url_succeeds_and_says_the_url_could_not_be_read()
    {
        await LayoutAsync(answers: false);

        var opened = await Handler.HandleAsync(Command());

        Assert.True(opened.Succeeded, opened.Error);
        Assert.Contains("could not read its URL", opened.Value);
        Assert.DoesNotContain("http", opened.Value);
        Assert.True(await HasRefAsync(Forge, "refs/for/main"));
    }

    [Fact]
    public async Task The_push_goes_to_origin_and_never_to_the_other_remotes()
    {
        await LayoutAsync();
        await ForgeRepositoryAsync(Other);
        await RunAsync(Container, "remote", "add", "elsewhere", Other);

        var opened = await Handler.HandleAsync(Command());

        Assert.True(opened.Succeeded, opened.Error);
        Assert.True(await HasRefAsync(Forge, "refs/heads/feat/x"));
        Assert.False(await HasRefAsync(Other, "refs/heads/feat/x"));
        Assert.Empty(Lines(Other, "refs-pushed.txt"));
    }

    [Fact]
    public async Task A_repository_that_sets_another_push_remote_is_pushed_there_and_not_to_origin()
    {
        await LayoutAsync();
        await ForgeRepositoryAsync(Other);
        await RunAsync(Container, "remote", "add", "elsewhere", Other);
        await RunAsync(Container, "push", "elsewhere", "main");
        File.Delete(Path.Combine(Other, "refs-pushed.txt"));

        var opened = await Handler.HandleAsync(Command(pushRemote: "elsewhere"));

        Assert.True(opened.Succeeded, opened.Error);
        Assert.Equal(["refs/heads/feat/x", "refs/for/main"], Lines(Other, "refs-pushed.txt"));
        Assert.False(await HasRefAsync(Forge, "refs/heads/feat/x"));
        Assert.Empty(Lines(Forge, "refs-pushed.txt"));
    }

    [Theory]
    [InlineData("github")]
    [InlineData("forgejo")]
    public async Task A_mirror_whose_push_url_is_disabled_is_refused_and_nothing_is_pushed_or_run(string forge)
    {
        await LayoutAsync();

        var opened = await Handler.HandleAsync(Command(forge, pushRemote: "github"));

        Assert.False(opened.Succeeded);
        Assert.Contains("'github'", opened.Error);
        Assert.Contains("disabled", opened.Error);
        Assert.Contains("Nothing was pushed", opened.Error);
        Assert.Empty(Lines(Forge, "refs-pushed.txt"));
        Assert.Empty(_processes.Runs);
        Assert.Empty(_probe.Hosts);
    }

    [Fact]
    public async Task An_origin_whose_push_url_is_disabled_is_refused_too()
    {
        await LayoutAsync();
        await RunAsync(Container, "remote", "set-url", "--push", "origin", "disabled");

        var opened = await Handler.HandleAsync(Command());

        Assert.False(opened.Succeeded);
        Assert.Contains("fleet never pushes to it", opened.Error);
        Assert.Empty(Lines(Forge, "refs-pushed.txt"));
    }

    [Fact]
    public async Task A_remote_the_repository_does_not_have_is_refused_naming_the_setting()
    {
        await LayoutAsync();

        var opened = await Handler.HandleAsync(Command(pushRemote: "upstream"));

        Assert.False(opened.Succeeded);
        Assert.Contains("no remote named 'upstream'", opened.Error);
        Assert.Contains("repositories.api.pushRemote", opened.Error);
        Assert.Empty(Lines(Forge, "refs-pushed.txt"));
    }

    [Fact]
    public async Task A_forge_fleet_cannot_tell_fails_before_anything_is_pushed()
    {
        await LayoutAsync();

        var opened = await Handler.HandleAsync(Command(forge: null));

        Assert.False(opened.Succeeded);
        Assert.Contains("repositories.api.forge", opened.Error);
        Assert.Empty(Lines(Forge, "refs-pushed.txt"));
    }

    [Theory]
    [InlineData("missing.md", "does not exist")]
    [InlineData("", "body file is empty")]
    public async Task A_body_file_fleet_cannot_read_is_refused_and_nothing_is_pushed(string file, string reason)
    {
        await LayoutAsync();

        var opened = await Handler.HandleAsync(Command(bodyFile: file));

        Assert.False(opened.Succeeded);
        Assert.Contains(reason, opened.Error);
        Assert.Empty(Lines(Forge, "refs-pushed.txt"));
    }

    [Fact]
    public async Task An_empty_body_file_and_a_branch_that_is_not_local_are_refused()
    {
        await LayoutAsync();
        File.WriteAllText(BodyFile, " \n");

        Assert.Contains("is empty", (await Handler.HandleAsync(Command())).Error);

        File.WriteAllText(BodyFile, "text");

        var unknown = await Handler.HandleAsync(Command(branch: "feat/nowhere"));

        Assert.Contains("not a local branch", unknown.Error);
        Assert.Contains("is not a usable branch", (await Handler.HandleAsync(Command(branch: "--force"))).Error);
        Assert.Contains("nothing to open", (await Handler.HandleAsync(Command(branch: "main"))).Error);
        Assert.Empty(Lines(Forge, "refs-pushed.txt"));
    }

    [Fact]
    public async Task A_body_file_is_read_relative_to_the_branch_worktree()
    {
        await LayoutAsync();

        var worktree = Path.Combine(Container, "feat_x");

        await RunAsync(Container, "worktree", "add", worktree, "feat/x");
        Directory.CreateDirectory(Path.Combine(worktree, ".fleet"));
        File.WriteAllText(Path.Combine(worktree, ".fleet", "pr.md"), "From the worktree.");

        var opened = await Handler.HandleAsync(Command(bodyFile: Path.Combine(".fleet", "pr.md")));

        Assert.True(opened.Succeeded, opened.Error);
        Assert.Contains("description=From the worktree.", Lines(Forge, "push-options.txt"));
    }

    // The GitHub path, with git scripted: a github.com push URL cannot point at a temp folder.
    private static ScriptedGit GitHubGit(string pushUrl = "git@github.com:acme/api.git") => new()
    {
        Answers = { ["remote get-url --push --all origin"] = new GitResult(0, pushUrl + "\n", string.Empty) },
    };

    [Fact]
    public async Task A_github_repository_gets_the_branch_pushed_then_gh_pr_create_with_the_body_file()
    {
        await LayoutAsync();

        var git = GitHubGit();
        var opened = await new OpenPrHandler(git, _processes, _probe).HandleAsync(Command(forge: null));

        Assert.True(opened.Succeeded, opened.Error);
        Assert.StartsWith("https://github.com/acme/api/pull/7", opened.Value);

        var push = Assert.Single(git.Pushes);

        Assert.Equal(["push", "--set-upstream", "origin", "refs/heads/feat/x:refs/heads/feat/x"], push);

        Assert.Equal(["--version"], _processes.Runs[0].Args);

        var (executable, args, workDir) = _processes.Runs[1];

        Assert.Equal(2, _processes.Runs.Count);
        Assert.Equal("gh", executable);
        Assert.Equal(
            [
                "pr", "create", "--repo", "acme/api", "--base", "main", "--head", "feat/x",
                "--title", "feat: open pull requests", "--body-file", BodyFile,
            ],
            args);
        Assert.Equal(Container, workDir);
        Assert.Empty(_probe.Hosts);
        Assert.DoesNotContain(git.Runs, r => r.Any(a => a.Contains("refs/for/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task No_command_fleet_runs_merges_forces_or_deletes()
    {
        await LayoutAsync();

        var git = GitHubGit();

        await new OpenPrHandler(git, _processes, _probe).HandleAsync(Command(forge: null));
        await new OpenPrHandler(git, _processes, _probe).HandleAsync(Command(forge: "gitea"));

        var words = git.Runs.Concat(_processes.Runs.Select(r => r.Args)).SelectMany(a => a).ToList();

        Assert.Equal(3, git.Pushes.Count());
        Assert.Single(_processes.Creates);

        Assert.DoesNotContain(words, w => w is "merge" or "--force" or "-f" or "--delete" or "-d" or "--force-with-lease");
        Assert.DoesNotContain(words, w => w.StartsWith('+') || w.StartsWith(':') || w.Contains("force-push", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_branch_push_runs_no_gh_and_a_failed_gh_says_the_branch_was_pushed()
    {
        await LayoutAsync();

        var refusing = GitHubGit();

        refusing.Answers["push"] = new GitResult(1, string.Empty, "! [rejected] feat/x -> feat/x (non-fast-forward)");

        var rejected = await new OpenPrHandler(refusing, _processes, _probe).HandleAsync(Command(forge: null));

        Assert.False(rejected.Succeeded);
        Assert.Contains("non-fast-forward", rejected.Error);
        Assert.Empty(_processes.Creates);

        _processes.Answer = new ProcessResult(1, string.Empty, "pull request create failed: GraphQL: No commits between main and feat/x");

        var refused = await new OpenPrHandler(GitHubGit(), _processes, _probe).HandleAsync(Command(forge: null));

        Assert.False(refused.Succeeded);
        Assert.Contains("Pushed feat/x to origin", refused.Error);
        Assert.Contains("No commits between", refused.Error);
    }

    [Fact]
    public async Task A_pull_request_that_already_exists_on_github_is_a_success_with_its_url()
    {
        await LayoutAsync();

        _processes.Answer = new ProcessResult(
            1,
            string.Empty,
            "a pull request for branch \"feat/x\" into branch \"main\" already exists:\nhttps://github.com/acme/api/pull/7\n");

        var existing = await new OpenPrHandler(GitHubGit(), _processes, _probe).HandleAsync(Command(forge: null));

        Assert.True(existing.Succeeded, existing.Error);
        Assert.StartsWith("https://github.com/acme/api/pull/7", existing.Value);
        Assert.Contains("already existed", existing.Value);
    }

    [Fact]
    public async Task Without_gh_nothing_is_pushed()
    {
        await LayoutAsync();

        var git = GitHubGit();

        _processes.Installed = false;

        var missing = await new OpenPrHandler(git, _processes, _probe).HandleAsync(Command(forge: null));

        Assert.False(missing.Succeeded);
        Assert.Contains("not installed", missing.Error);
        Assert.Contains("Nothing was pushed", missing.Error);
        Assert.Empty(git.Pushes);
    }

    [Theory]
    [InlineData("push --set-upstream", "It may or may not have been pushed")]
    [InlineData("push origin refs/heads/feat/x:refs/for/main", "check whether the pull request exists")]
    public async Task A_push_that_never_answers_is_stopped_and_reported(string hanging, string state)
    {
        await LayoutAsync();

        var git = GitHubGit("git@git.example.test:acme/api.git");

        git.Hangs = hanging;

        var stuck = await new OpenPrHandler(git, _processes, _probe, TimeSpan.FromMilliseconds(50))
            .HandleAsync(Command())
            .WaitAsync(TimeSpan.FromSeconds(30));

        Assert.False(stuck.Succeeded);
        Assert.Contains("did not finish within", stuck.Error);
        Assert.Contains(state, stuck.Error);
    }

    [Fact]
    public async Task A_caller_that_cancels_gets_the_cancellation_not_a_timeout_message()
    {
        await LayoutAsync();

        var git = GitHubGit("git@git.example.test:acme/api.git");

        git.Hangs = "push --set-upstream";

        using var cancelled = new CancellationTokenSource();

        var pending = new OpenPrHandler(git, _processes, _probe).HandleAsync(Command(), cancelled.Token);

        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task A_body_too_long_for_a_push_option_is_refused_before_anything_is_pushed_and_github_takes_it()
    {
        await LayoutAsync();
        File.WriteAllText(BodyFile, string.Concat(Enumerable.Repeat("0123456789\n", 1819)));

        var agit = await Handler.HandleAsync(Command());

        Assert.False(agit.Succeeded);
        Assert.Contains("larger than 20000 bytes", agit.Error);
        Assert.Contains("Nothing was pushed", agit.Error);
        Assert.Empty(Lines(Forge, "refs-pushed.txt"));

        var github = await new OpenPrHandler(GitHubGit(), _processes, _probe).HandleAsync(Command(forge: null));

        Assert.True(github.Succeeded, github.Error);

        File.WriteAllText(BodyFile, new string('x', OpenPrHandler.MaxBodyBytes + 1));

        Assert.Contains(
            "larger than 60000 bytes",
            (await new OpenPrHandler(GitHubGit(), _processes, _probe).HandleAsync(Command(forge: null))).Error);
    }

    [Fact]
    public async Task The_largest_body_a_push_option_takes_arrives_whole()
    {
        await LayoutAsync();

        var text = string.Concat(Enumerable.Repeat("0123456789\n", 1818));

        File.WriteAllText(BodyFile, text);

        var opened = await Handler.HandleAsync(Command());

        Assert.True(opened.Succeeded, opened.Error);

        var description = Lines(Forge, "push-options.txt")[2];

        Assert.Equal(
            text.TrimEnd('\n'),
            Encoding.UTF8.GetString(Convert.FromBase64String(description["description={base64}".Length..])));
    }

    [Fact]
    public async Task A_body_file_outside_the_project_folder_is_refused_even_through_a_link()
    {
        await LayoutAsync();

        var outside = Path.Combine(Path.GetDirectoryName(_root)!, "secret.txt");

        File.WriteAllText(outside, "a private key");

        var direct = await Handler.HandleAsync(Command(bodyFile: outside));

        Assert.False(direct.Succeeded);
        Assert.Contains("outside the project folder", direct.Error);

        var climbing = await Handler.HandleAsync(Command(bodyFile: Path.Combine("..", "..", "secret.txt")));

        Assert.Contains("outside the project folder", climbing.Error);

        if (!OperatingSystem.IsWindows())
        {
            var link = Path.Combine(_root, "linked.md");

            File.CreateSymbolicLink(link, outside);

            Assert.Contains("outside the project folder", (await Handler.HandleAsync(Command(bodyFile: link))).Error);
        }

        Assert.Empty(Lines(Forge, "refs-pushed.txt"));
    }

    [Fact]
    public async Task A_remote_with_two_push_urls_is_refused_because_git_would_push_to_both()
    {
        await LayoutAsync();
        await ForgeRepositoryAsync(Other);
        await RunAsync(Container, "remote", "set-url", "--add", "--push", "origin", Forge);
        await RunAsync(Container, "remote", "set-url", "--add", "--push", "origin", Other);

        var opened = await Handler.HandleAsync(Command());

        Assert.False(opened.Succeeded);
        Assert.Contains("has 2 push URLs", opened.Error);
        Assert.Empty(Lines(Forge, "refs-pushed.txt"));
        Assert.Empty(Lines(Other, "refs-pushed.txt"));
    }

    [Fact]
    public async Task A_branch_and_a_tag_of_one_name_still_open_the_pull_request_from_the_branch()
    {
        await LayoutAsync();
        await RunAsync(Container, "tag", "feat/x", "main");

        var opened = await Handler.HandleAsync(Command());

        Assert.True(opened.Succeeded, opened.Error);
        Assert.Equal(["refs/heads/feat/x", "refs/for/main"], Lines(Forge, "refs-pushed.txt"));
    }

    [Fact]
    public async Task A_host_that_answers_as_forgejo_gets_the_agit_push_without_any_setting()
    {
        await LayoutAsync();

        var git = GitHubGit("git@git.example.test:acme/api.git");

        git.Answers["push origin refs/heads/feat/x:refs/for/main"] = new GitResult(
            0, string.Empty, "remote: Visit the existing pull request: https://git.example.test/acme/api/pulls/9\n");
        _probe.Answer = ForgeProbeResult.ForgejoOrGitea;

        var opened = await new OpenPrHandler(git, _processes, _probe).HandleAsync(Command(forge: null));

        Assert.True(opened.Succeeded, opened.Error);
        Assert.StartsWith("https://git.example.test/acme/api/pulls/9", opened.Value);
        Assert.Equal(["git.example.test"], _probe.Hosts);
        Assert.Equal(2, git.Pushes.Count());
        Assert.Empty(_processes.Runs);
    }

    [Fact]
    public async Task Without_a_url_the_result_gives_the_pull_request_number_and_the_web_address_it_knows()
    {
        await LayoutAsync();

        var git = GitHubGit("https://git.example.test/acme/api.git");

        git.Answers["push origin refs/heads/feat/x:refs/for/main"] = new GitResult(
            0, string.Empty, " * [new reference]     feat/x -> refs/pull/12/head\n");

        var opened = await new OpenPrHandler(git, _processes, _probe).HandleAsync(Command());

        Assert.True(opened.Succeeded, opened.Error);
        Assert.Contains("could not read its URL", opened.Value);
        Assert.Contains("pull request #12", opened.Value);
        Assert.Contains("https://git.example.test/acme/api/pulls.", opened.Value);
        Assert.DoesNotContain("pulls/12", opened.Value);
    }
}
