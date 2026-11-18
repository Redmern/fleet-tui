using Fleet.Features.Repositories.OpenPr.Models;
using Fleet.Ports.Forge;
using Fleet.Ports.Git;
using Fleet.Shared.Results;
using Fleet.Shared.Settings.Enums;

namespace Fleet.Features.Repositories.OpenPr;

public sealed class OpenPrHandler(
    IGitRunner git, IProcessRunner processes, IForgeProbe probe, TimeSpan? commandTimeout = null)
{
    public const int MaxBodyBytes = 60_000;

    public const int MaxPushOptionBodyBytes = 20_000;

    public static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(120);

    private readonly TimeSpan _commandTimeout = commandTimeout ?? DefaultCommandTimeout;

    public const string GitHubCli = "gh";

    public async Task<Result<string>> HandleAsync(OpenPrCommand command, CancellationToken ct = default)
    {
        if (!Directory.Exists(command.Container))
        {
            return Result<string>.Fail($"{command.Container} does not exist.");
        }

        var branch = command.Branch.Trim();
        var @base = command.Base.Trim();
        var title = AgitPush.OneLine(command.Title);
        var workDir = RepositoryWorktree.For(command.Container, branch, Directory.Exists);

        if (GitNames.Refusal("branch", branch) is { } badBranch)
        {
            return Result<string>.Fail(badBranch);
        }

        if (GitNames.Refusal("base branch", @base) is { } badBase)
        {
            return Result<string>.Fail(badBase);
        }

        if (string.Equals(branch, @base, StringComparison.Ordinal))
        {
            return Result<string>.Fail($"The branch and the base are both '{branch}': there is nothing to open.");
        }

        if (title.Length == 0)
        {
            return Result<string>.Fail("The title is empty.");
        }

        var body = Body(command.BodyFile, workDir, command.Root);

        if (!body.Succeeded)
        {
            return Result<string>.Fail(body.Error!);
        }

        var known = await git
            .RunAsync(command.Container, ["rev-parse", "--verify", "--quiet", $"refs/heads/{branch}"], null, ct)
            .ConfigureAwait(false);

        if (!known.Ok)
        {
            return Result<string>.Fail($"'{branch}' is not a local branch of {command.Repository}. Nothing was pushed.");
        }

        var pushUrl = await PushRemote
            .UrlAsync(git, command.Container, command.Repository, command.Settings, ct)
            .ConfigureAwait(false);

        if (!pushUrl.Succeeded)
        {
            return Result<string>.Fail(pushUrl.Error!);
        }

        var forge = await ForgeDecision
            .DecideAsync(command.Repository, command.Settings, pushUrl.Value, probe, ct)
            .ConfigureAwait(false);

        if (!forge.Succeeded)
        {
            return Result<string>.Fail(forge.Error!);
        }

        var remote = command.Settings.Remote;
        var address = RemoteAddress.Parse(pushUrl.Value);

        if (forge.Value == ForgeKind.GitHub && address is not { Slug.Length: > 0 })
        {
            return Result<string>.Fail(
                $"The push URL '{RemoteAddress.Redacted(pushUrl.Value)}' of remote '{remote}' names no GitHub "
                + "repository. Nothing was pushed.");
        }

        if (forge.Value != ForgeKind.GitHub && body.Value.Bytes > MaxPushOptionBodyBytes)
        {
            return Result<string>.Fail(
                $"The body file {body.Value.Path} is larger than {MaxPushOptionBodyBytes} bytes, the most a pull "
                + "request push can carry. Shorten it. Nothing was pushed.");
        }

        if (forge.Value == ForgeKind.GitHub)
        {
            var installed = await RunAsync(
                limit => processes.RunAsync(GitHubCli, ["--version"], workDir, limit), ct).ConfigureAwait(false);

            if (installed is not { Ok: true })
            {
                return Result<string>.Fail(
                    "The GitHub command line (gh) is not installed or not on PATH, so fleet cannot open the pull "
                    + "request. Nothing was pushed.");
            }
        }

        var pushed = await RunAsync(
            limit => git.RunAsync(
                workDir, ["push", "--set-upstream", remote, $"refs/heads/{branch}:refs/heads/{branch}"], string.Empty, limit),
            ct).ConfigureAwait(false);

        if (pushed is null)
        {
            return Result<string>.Fail(TimedOut($"git push {remote} {branch}", "It may or may not have been pushed."));
        }

        if (!pushed.Ok)
        {
            return Result<string>.Fail($"git push {remote} {branch} failed: {pushed.Message}");
        }

        return forge.Value == ForgeKind.GitHub
            ? await GitHubAsync(workDir, address!, remote, branch, @base, title, body.Value.Path, ct).ConfigureAwait(false)
            : await AgitAsync(workDir, address, remote, branch, @base, title, body.Value.Text, ct).ConfigureAwait(false);
    }

    private async Task<Result<string>> GitHubAsync(
        string workDir, RemoteAddress address, string remote, string branch, string @base, string title, string bodyFile,
        CancellationToken ct)
    {
        var created = await RunAsync(
            limit => processes.RunAsync(
                GitHubCli,
                ["pr", "create", "--repo", address.GitHubRepository, "--base", @base, "--head", branch, "--title", title, "--body-file", bodyFile],
                workDir,
                limit),
            ct).ConfigureAwait(false);

        if (created is null)
        {
            return Result<string>.Fail(
                TimedOut("gh (opening the pull request)", $"{branch} was pushed to {remote}; check whether the pull request exists."));
        }

        if (!created.Ok && PrUrl.Find(created.StdErr + "\n" + created.StdOut) is { } existing)
        {
            return Result<string>.Ok(
                $"{existing}\nPushed {branch} to {remote}. The pull request already existed, so the push updated "
                + "it; its title and text were left as they are. fleet did not merge it.");
        }

        if (!created.Ok)
        {
            return Result<string>.Fail(
                $"Pushed {branch} to {remote}, but gh could not open the pull request: {created.Message}");
        }

        return Result<string>.Ok(
            PrUrl.Find(created.StdOut) is { } url
                ? Opened(url, branch, remote, @base)
                : NoUrl(branch, remote, @base, $"https://{address.Authority}/{address.Slug}/pulls"));
    }

    private async Task<Result<string>> AgitAsync(
        string workDir, RemoteAddress? address, string remote, string branch, string @base, string title, string body,
        CancellationToken ct)
    {
        var opened = await RunAsync(
            limit => git.RunAsync(workDir, AgitPush.Args(remote, branch, @base, title, body), string.Empty, limit),
            ct).ConfigureAwait(false);

        if (opened is null)
        {
            return Result<string>.Fail(
                TimedOut(
                    $"the pull request push ({branch}:refs/for/{@base})",
                    $"{branch} was pushed to {remote}; check whether the pull request exists."));
        }

        if (!opened.Ok)
        {
            return Result<string>.Fail(
                $"Pushed {branch} to {remote}, but the pull request push ({branch}:refs/for/{@base}) failed: "
                + opened.Message);
        }

        var output = opened.StdErr + "\n" + opened.StdOut;

        if (PrUrl.Find(output) is { } url)
        {
            return Result<string>.Ok(Opened(url, branch, remote, @base));
        }

        var web = address?.WebBase is { } site ? site + "/pulls" : null;

        return Result<string>.Ok(
            PrUrl.Number(output) is { } number
                ? NoUrl(branch, remote, @base, web, $" It is pull request #{number}.")
                : NoUrl(branch, remote, @base, web));
    }

    private static string Opened(string url, string branch, string remote, string @base) =>
        $"{url}\nPushed {branch} to {remote} and opened or updated the pull request into {@base}. "
        + "fleet did not merge it: merging is the user's.";

    private static string NoUrl(string branch, string remote, string @base, string? web, string extra = "") =>
        $"Pushed {branch} to {remote} and the pull request into {@base} was sent, but fleet could not read its URL "
        + $"from the output.{extra}"
        + (web is null ? " Look for it in the forge's web UI." : $" Look for it at {web}.")
        + " fleet did not merge it.";

    private string TimedOut(string what, string state) =>
        $"{what} did not finish within {(int)_commandTimeout.TotalSeconds} s and was stopped (does it wait for a "
        + $"password or a passphrase?). {state}";

    private async Task<T?> RunAsync<T>(Func<CancellationToken, Task<T>> run, CancellationToken ct)
        where T : class
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(_commandTimeout);

        try
        {
            return await run(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private static bool Inside(string path, string folder)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(folder), path);

        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static Result<(string Path, string Text, long Bytes)> Body(string bodyFile, string workDir, string root)
    {
        var given = bodyFile.Trim();

        if (given.Length == 0)
        {
            return Result<(string, string, long)>.Fail("The body file is empty.");
        }

        try
        {
            var path = Path.GetFullPath(given, workDir);

            if (!File.Exists(path))
            {
                return Result<(string, string, long)>.Fail($"The body file {path} does not exist. Nothing was pushed.");
            }

            var file = new FileInfo(path);
            var real = file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;

            if (root.Trim().Length == 0 || !Inside(path, root) || !Inside(real, root))
            {
                return Result<(string, string, long)>.Fail(
                    $"The body file {path} is outside the project folder {root}: fleet only sends a file from "
                    + "inside the project as the pull request's text. Nothing was pushed.");
            }

            var bytes = new FileInfo(real).Length;

            if (bytes > MaxBodyBytes)
            {
                return Result<(string, string, long)>.Fail(
                    $"The body file {path} is larger than {MaxBodyBytes} bytes. Nothing was pushed.");
            }

            var text = File.ReadAllText(path);

            return text.Trim().Length == 0
                ? Result<(string, string, long)>.Fail($"The body file {path} is empty. Nothing was pushed.")
                : Result<(string, string, long)>.Ok((path, text, bytes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result<(string, string, long)>.Fail(
                $"fleet cannot read the body file {given}: {e.Message} Nothing was pushed.");
        }
    }
}
