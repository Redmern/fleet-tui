using Fleet.Features.Setup.WriteInclude;
using Fleet.Features.Setup.WriteInclude.Enums;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.Features.Setup;

public sealed class WriteIncludeTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "fleet-include\\" + Path.GetRandomFileName());

    private string Config => Path.Combine(_home, ".config", "fleet");

    private string Include => OrchestratorInclude.PathIn(Config);

    private SortedDictionary<string, string> Snapshot() =>
        new(Directory.GetFiles(_home, "*", SearchOption.AllDirectories)
            .ToDictionary(f => f, f => Convert.ToBase64String(File.ReadAllBytes(f))), StringComparer.Ordinal);

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    [Fact]
    public void A_missing_file_is_written_with_the_current_text_and_its_folder_is_made()
    {
        var outcome = IncludeWriter.Write(Config);

        Assert.True(outcome.Succeeded, outcome.Error);
        Assert.Equal(IncludeWrite.Written, outcome.Value);
        Assert.Equal(OrchestratorInclude.Text, File.ReadAllText(Include));
    }

    [Fact]
    public void A_file_with_the_current_text_is_left_alone_and_keeps_its_time()
    {
        IncludeWriter.Write(Config);
        var stamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Include, stamp);

        var outcome = IncludeWriter.Write(Config);

        Assert.Equal(IncludeWrite.Unchanged, outcome.Value);
        Assert.Equal(stamp, File.GetLastWriteTimeUtc(Include));
    }

    [Fact]
    public void An_edited_file_is_overwritten_with_the_current_text()
    {
        IncludeWriter.Write(Config);
        File.WriteAllText(Include, "# my own notes\n");

        var outcome = IncludeWriter.Write(Config);

        Assert.Equal(IncludeWrite.Written, outcome.Value);
        Assert.Equal(OrchestratorInclude.Text, File.ReadAllText(Include));
    }

    [Fact]
    public void Writing_leaves_no_temporary_file_behind()
    {
        IncludeWriter.Write(Config);
        File.WriteAllText(Include, "old");
        IncludeWriter.Write(Config);

        Assert.Equal([Include], Directory.GetFiles(Config));
    }

    [Fact]
    public void The_users_own_instruction_files_keep_their_bytes()
    {
        string[] own =
        [
            Path.Combine(_home, "CLAUDE.md"),
            Path.Combine(_home, "AGENTS.md"),
            Path.Combine(_home, ".claude", "CLAUDE.md"),
            Path.Combine(_home, ".codex", "AGENTS.md"),
            Path.Combine(_home, ".config", "opencode", "AGENTS.md"),
            Path.Combine(_home, ".pi", "agent", "AGENTS.md"),
        ];

        foreach (var file in own)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, [.. "# mine\r\n@~/.config/fleet/ORCHESTRATOR.md\n"u8, 0xEF, 0xBB, 0xBF]);
        }

        var before = Snapshot();

        IncludeWriter.Write(Config);
        File.WriteAllText(Include, "edited");
        IncludeWriter.Write(Config);

        var after = Snapshot();
        after.Remove(Include);

        Assert.Equal(before, after);
    }

    [Fact]
    public void Writing_creates_no_instruction_file_of_the_user()
    {
        IncludeWriter.Write(Config);

        Assert.Equal([Include], Directory.GetFiles(_home, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_folder_that_cannot_be_made_is_a_failure_with_the_reason()
    {
        Directory.CreateDirectory(_home);
        var blocker = Path.Combine(_home, "not-a-folder");
        File.WriteAllText(blocker, "a file where the config folder should be");

        var outcome = IncludeWriter.Write(Path.Combine(blocker, "fleet"));

        Assert.False(outcome.Succeeded);
        Assert.Contains(OrchestratorInclude.FileName, outcome.Error);
    }

    [Fact]
    public void A_malformed_config_home_is_a_failure_not_a_crash()
    {
        var outcome = IncludeWriter.Write(Path.Combine(_home, "bad\0name"));

        Assert.False(outcome.Succeeded);
    }
}
