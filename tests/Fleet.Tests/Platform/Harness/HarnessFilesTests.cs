using System.Text.Json.Nodes;
using Fleet.Platform.Codex;
using Fleet.Platform.Harness;
using Fleet.Platform.Pi;
using Fleet.Ports.Claude.Models;
using Fleet.Shared.Results;
using Fleet.Tests.Platform.Codex;

namespace Fleet.Tests.Platform.Harness;

public sealed class HarnessFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("fleet-harness-files-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string File_ => Path.Combine(_root, "home", "config.toml");

    private static McpServerEntry Server(string project, string caller) =>
        new("fleet", "/bin/fleet", ["mcp", "--project", project, "--caller", caller]);

    private static void NoExcludes(string folder, IReadOnlyList<string> patterns)
    {
    }

    private string Worktree(int number)
    {
        var folder = Path.Combine(_root, $"w\\t{number}");
        Directory.CreateDirectory(folder);

        return folder;
    }

    [Fact]
    public async Task Two_edits_at_the_same_time_both_keep_their_change()
    {
        using var firstReads = new ManualResetEventSlim();
        using var secondReads = new ManualResetEventSlim();

        var first = Task.Run(() => HarnessFiles.Edit(File_, text =>
        {
            firstReads.Set();
            secondReads.Wait(TimeSpan.FromMilliseconds(300));
            return Result<string>.Ok(text + "first\n");
        }));

        Assert.True(firstReads.Wait(TimeSpan.FromSeconds(5)));

        var second = Task.Run(() => HarnessFiles.Edit(File_, text =>
        {
            secondReads.Set();
            return Result<string>.Ok(text + "second\n");
        }));

        Assert.True((await first).Succeeded);
        Assert.True((await second).Succeeded);
        Assert.Equal("first\nsecond\n", File.ReadAllText(File_));
    }

    [Fact]
    public async Task An_edit_waits_for_another_process_that_holds_the_lock_of_the_file()
    {
        var lockFile = HarnessFiles.LockFor(File_);
        Directory.CreateDirectory(Path.GetDirectoryName(lockFile)!);
        Task<Result> edit;

        using (new FileStream(lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            edit = Task.Run(() => HarnessFiles.Edit(File_, text => Result<string>.Ok(text + "mine\n")));
            await Task.Delay(150);

            Assert.False(edit.IsCompleted);
            Assert.False(File.Exists(File_));
        }

        Assert.True((await edit).Succeeded);
        Assert.Equal("mine\n", File.ReadAllText(File_));
    }

    [Fact]
    public void An_edit_leaves_no_other_file_next_to_the_one_it_wrote()
    {
        HarnessFiles.Edit(File_, _ => Result<string>.Ok("a\n"));
        HarnessFiles.Edit(File_, _ => Result<string>.Ok("b\n"));

        Assert.Equal([File_], Directory.GetFiles(Path.GetDirectoryName(File_)!));
    }

    [Fact]
    public void A_refused_change_leaves_the_file_and_names_it()
    {
        HarnessFiles.Edit(File_, _ => Result<string>.Ok("a\n"));

        var refused = HarnessFiles.Edit(File_, _ => Result<string>.Fail("it is odd"));

        Assert.False(refused.Succeeded);
        Assert.Equal($"{File_} was left untouched: it is odd.", refused.Error);
        Assert.Equal("a\n", File.ReadAllText(File_));
    }

    [Fact]
    public void Codex_agents_started_together_are_all_trusted_in_the_user_config()
    {
        var folders = Enumerable.Range(0, 16).Select(Worktree).ToList();
        var config = new CodexHarnessConfig(Server, new Dictionary<string, string>(), NoExcludes, File_);

        var written = folders.AsParallel().WithDegreeOfParallelism(16)
            .Select(folder => config.WriteForAgent(folder, "proj", "api", Path.GetFileName(folder)))
            .ToList();

        Assert.All(written, result => Assert.True(result.Succeeded, result.Error));
        var text = File.ReadAllText(File_);
        Assert.All(folders, folder => Assert.Contains($"[projects.{TomlKey.Quoted(folder)}]", text));
    }

    [Fact]
    public void Pi_agents_started_together_are_all_trusted_in_the_trust_store()
    {
        var folders = Enumerable.Range(0, 16).Select(Worktree).ToList();
        var store = Path.Combine(_root, "home", "trust.json");
        var config = new PiHarnessConfig(Server, new Dictionary<string, string>(), NoExcludes, store);

        var written = folders.AsParallel().WithDegreeOfParallelism(16)
            .Select(folder => config.WriteForAgent(folder, "proj", "api", Path.GetFileName(folder)))
            .ToList();

        Assert.All(written, result => Assert.True(result.Succeeded, result.Error));
        var trusted = JsonNode.Parse(File.ReadAllText(store))!.AsObject();
        Assert.All(folders, folder => Assert.True((bool?)trusted[HarnessFiles.Canonical(folder)], folder));
    }
}
