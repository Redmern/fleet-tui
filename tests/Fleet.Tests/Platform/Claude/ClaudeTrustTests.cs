using System.Text.Json;
using Fleet.Platform.Claude;

namespace Fleet.Tests.Platform.Claude;

public sealed class ClaudeTrustTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly string _claudeJson;

    public ClaudeTrustTests()
    {
        Directory.CreateDirectory(_dir);
        _claudeJson = Path.Combine(_dir, ".claude.json");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private JsonElement Reload()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(_claudeJson));
        return doc.RootElement.Clone();
    }

    private static string Key(string folder) => Path.GetFullPath(folder).Replace('\\', '/');

    [Fact]
    public void It_creates_the_file_and_trusts_a_folder_when_none_exists()
    {
        var folder = Path.Combine(_dir, "worktree");

        Assert.True(new ClaudeConfigWriter().TrustFolder(_claudeJson, folder, "fleet").Succeeded);

        var projects = Reload().GetProperty("projects");
        var entry = projects.GetProperty(Key(folder));

        Assert.True(entry.GetProperty("hasTrustDialogAccepted").GetBoolean());

        var enabled = entry.GetProperty("enabledMcpjsonServers").EnumerateArray()
            .Select(e => e.GetString());

        Assert.Contains("fleet", enabled);
    }

    [Fact]
    public void It_preserves_other_top_level_keys_and_sibling_projects()
    {
        var other = Key(Path.Combine(_dir, "already"));
        var seed = $$"""
            {
              "userID": "keep-me",
              "projects": {
                "{{other}}": { "hasTrustDialogAccepted": true, "lastCost": 42 }
              }
            }
            """;
        File.WriteAllText(_claudeJson, seed);

        var folder = Path.Combine(_dir, "worktree");
        Assert.True(new ClaudeConfigWriter().TrustFolder(_claudeJson, folder, "fleet").Succeeded);

        var root = Reload();

        Assert.Equal("keep-me", root.GetProperty("userID").GetString());

        var projects = root.GetProperty("projects");
        Assert.True(projects.GetProperty(other).GetProperty("hasTrustDialogAccepted").GetBoolean());
        Assert.Equal(42, projects.GetProperty(other).GetProperty("lastCost").GetInt32());
        Assert.True(projects.GetProperty(Key(folder)).GetProperty("hasTrustDialogAccepted").GetBoolean());
    }

    [Fact]
    public void It_preserves_unknown_fields_on_the_trusted_entry()
    {
        var folder = Path.Combine(_dir, "worktree");
        var key = Key(folder);
        var seed = $$"""
            {
              "projects": {
                "{{key}}": { "hasTrustDialogAccepted": false, "lastSessionId": "abc" }
              }
            }
            """;
        File.WriteAllText(_claudeJson, seed);

        Assert.True(new ClaudeConfigWriter().TrustFolder(_claudeJson, folder, "fleet").Succeeded);

        var entry = Reload().GetProperty("projects").GetProperty(key);

        Assert.True(entry.GetProperty("hasTrustDialogAccepted").GetBoolean());
        Assert.Equal("abc", entry.GetProperty("lastSessionId").GetString());
    }

    [Fact]
    public void It_does_not_rewrite_when_already_trusted()
    {
        var folder = Path.Combine(_dir, "worktree");
        Assert.True(new ClaudeConfigWriter().TrustFolder(_claudeJson, folder, "fleet").Succeeded);

        File.SetAttributes(_claudeJson, FileAttributes.ReadOnly);

        try
        {
            Assert.True(new ClaudeConfigWriter().TrustFolder(_claudeJson, folder, "fleet").Succeeded);
        }
        finally
        {
            File.SetAttributes(_claudeJson, FileAttributes.Normal);
        }
    }

    [Fact]
    public void It_refuses_to_touch_invalid_json()
    {
        File.WriteAllText(_claudeJson, "{ not valid");

        Assert.False(new ClaudeConfigWriter().TrustFolder(_claudeJson, _dir, "fleet").Succeeded);
        Assert.Equal("{ not valid", File.ReadAllText(_claudeJson));
    }
}
