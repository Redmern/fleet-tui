using Fleet.Platform.Claude;
using Fleet.Ports.Claude.Models;

namespace Fleet.Tests.Platform.Claude;

public sealed class ClaudeConfigWriterTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    public ClaudeConfigWriterTests() => Directory.CreateDirectory(_dir);

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

    private string McpPath => Path.Combine(_dir, ".mcp.json");

    private string SettingsPath => Path.Combine(_dir, ".claude", "settings.local.json");

    private ClaudePlan Plan(
        IReadOnlyList<string>? allow = null,
        IReadOnlyList<string>? deny = null,
        IReadOnlyList<string>? ask = null) =>
        new(
            _dir,
            new McpServerEntry("fleet", "fleet", ["mcp", "--project", "techweb"]),
            allow ?? ["mcp__fleet__list_agents"],
            deny ?? [],
            ask ?? [],
            ["fleet"],
            "fleet.exe",
            ["hook-dispatch", "--project", "techweb"]);

    [Fact]
    public void The_server_is_registered_as_a_stdio_command()
    {
        Assert.True(new ClaudeConfigWriter().Sync(Plan()).Succeeded);

        var mcp = File.ReadAllText(McpPath);

        Assert.Contains("\"fleet\"", mcp);
        Assert.Contains("\"type\": \"stdio\"", mcp);
        Assert.Contains("\"--project\"", mcp);
    }

    [Fact]
    public void The_server_is_enabled_so_the_first_run_prompt_is_suppressed()
    {
        new ClaudeConfigWriter().Sync(Plan());

        Assert.Contains("\"enabledMcpjsonServers\"", File.ReadAllText(SettingsPath));
        Assert.Contains("mcp__fleet__list_agents", File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void Another_teams_mcp_server_and_unknown_keys_survive_the_merge()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(
            McpPath,
            """{"mcpServers":{"postgres":{"type":"stdio","command":"pg"}},"customKey":42}""");

        new ClaudeConfigWriter().Sync(Plan());

        var mcp = File.ReadAllText(McpPath);

        Assert.Contains("postgres", mcp);
        Assert.Contains("\"customKey\": 42", mcp);
        Assert.Contains("fleet", mcp);
    }

    [Fact]
    public void A_users_own_permissions_and_unknown_settings_survive_the_merge()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(
            SettingsPath,
            """{"permissions":{"allow":["Bash(ls)"]},"model":"opus"}""");

        new ClaudeConfigWriter().Sync(Plan());

        var settings = File.ReadAllText(SettingsPath);

        Assert.Contains("Bash(ls)", settings);
        Assert.Contains("\"model\": \"opus\"", settings);
        Assert.Contains("mcp__fleet__list_agents", settings);
    }

    [Fact]
    public void Re_syncing_does_not_pile_up_duplicate_fleet_rules()
    {
        var writer = new ClaudeConfigWriter();

        writer.Sync(Plan(allow: ["mcp__fleet__list_agents", "mcp__fleet__report"]));
        writer.Sync(Plan(allow: ["mcp__fleet__list_agents"]));

        var settings = File.ReadAllText(SettingsPath);

        Assert.Single(Occurrences(settings, "mcp__fleet__list_agents"));
        Assert.DoesNotContain("mcp__fleet__report", settings);
    }

    [Fact]
    public void The_fleet_server_is_only_enabled_once_even_after_repeated_syncs()
    {
        var writer = new ClaudeConfigWriter();

        writer.Sync(Plan());
        writer.Sync(Plan());

        Assert.Single(Occurrences(File.ReadAllText(SettingsPath), "\"fleet\""));
    }

    [Fact]
    public void The_dispatch_hook_is_registered_on_user_prompt_submit()
    {
        new ClaudeConfigWriter().Sync(Plan());

        var settings = File.ReadAllText(SettingsPath);

        Assert.Contains("\"UserPromptSubmit\"", settings);
        Assert.Contains("hook-dispatch", settings);
    }

    [Fact]
    public void Re_syncing_does_not_pile_up_duplicate_dispatch_hooks()
    {
        var writer = new ClaudeConfigWriter();

        writer.Sync(Plan());
        writer.Sync(Plan());

        Assert.Single(Occurrences(File.ReadAllText(SettingsPath), "hook-dispatch"));
    }

    [Fact]
    public void A_users_own_hooks_survive_the_merge()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(
            SettingsPath,
            """
            {"hooks":{"PostToolUse":[{"hooks":[{"type":"command","command":"echo done"}]}],
            "UserPromptSubmit":[{"hooks":[{"type":"command","command":"echo mine"}]}]}}
            """);

        new ClaudeConfigWriter().Sync(Plan());

        var settings = File.ReadAllText(SettingsPath);

        Assert.Contains("PostToolUse", settings);
        Assert.Contains("echo done", settings);
        Assert.Contains("echo mine", settings);
        Assert.Contains("hook-dispatch", settings);
    }

    [Fact]
    public void The_hook_state_is_reported_by_inspect()
    {
        new ClaudeConfigWriter().Sync(Plan());

        Assert.True(new ClaudeConfigWriter().Inspect(_dir, "fleet").HookInstalled);
    }

    private static McpServerEntry AgentServer() =>
        new("fleet", "fleet", ["mcp", "--project", "techweb", "--caller", "agent:backend/login"]);

    [Fact]
    public void Approving_a_worktree_registers_a_caller_stamped_server_but_no_hook()
    {
        var result = new ClaudeConfigWriter()
            .SyncWorktree(AgentServer(), _dir, ["mcp__fleet__list_agents"], [], []);

        Assert.True(result.Succeeded, result.Error);

        var settings = File.ReadAllText(SettingsPath);
        var mcp = File.ReadAllText(McpPath);

        Assert.Contains("\"enableAllProjectMcpServers\": true", settings);
        Assert.Contains("mcp__fleet__list_agents", settings);
        Assert.DoesNotContain("UserPromptSubmit", settings);
        Assert.Contains("agent:backend/login", mcp);
    }

    private System.Text.Json.JsonElement Hooks() =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(SettingsPath)).RootElement.GetProperty("hooks");

    private static int StatusHookCount(System.Text.Json.JsonElement groups) =>
        groups.EnumerateArray()
            .SelectMany(g => g.GetProperty("hooks").EnumerateArray())
            .Count(h => h.TryGetProperty("args", out var args) && args.GetArrayLength() > 0 && args[0].GetString() == "hook");

    [Fact]
    public void A_worktree_gets_a_status_hook_on_every_contract_event()
    {
        new ClaudeConfigWriter().SyncWorktree(AgentServer(), _dir, [], [], [], "C:/bin/fleet.exe");

        var hooks = Hooks();

        foreach (var name in Fleet.Shared.Hooks.HookStatus.Events)
        {
            Assert.Equal(1, StatusHookCount(hooks.GetProperty(name)));
        }

        var entry = hooks.GetProperty("PreToolUse")[0].GetProperty("hooks")[0];
        Assert.Equal("C:/bin/fleet.exe", entry.GetProperty("command").GetString());
        Assert.Equal("command", entry.GetProperty("type").GetString());
        Assert.True(entry.GetProperty("timeout").GetDouble() > 0);
        Assert.True(new ClaudeConfigWriter().Inspect(_dir, "fleet").StatusHooksInstalled);
    }

    [Fact]
    public void Re_syncing_status_hooks_does_not_pile_them_up()
    {
        var writer = new ClaudeConfigWriter();

        writer.SyncWorktree(AgentServer(), _dir, [], [], [], "fleet.exe");
        writer.SyncWorktree(AgentServer(), _dir, [], [], [], "fleet.exe");

        Assert.Equal(1, StatusHookCount(Hooks().GetProperty("Stop")));
    }

    [Fact]
    public void Status_hooks_merge_with_the_users_hooks_on_the_same_events()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(
            SettingsPath,
            """
            {"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo stopped","timeout":1.5,"async":true}]}],
            "PreToolUse":[{"matcher":"Bash","hooks":[{"type":"command","command":"lint"}]}],
            "PostToolUse":[{"hooks":[{"type":"command","command":"echo done"}]}]},
            "model":"opus"}
            """);

        new ClaudeConfigWriter().SyncWorktree(AgentServer(), _dir, [], [], [], "fleet.exe");

        var settings = File.ReadAllText(SettingsPath);

        Assert.Contains("echo stopped", settings);
        Assert.Contains("\"async\": true", settings);
        Assert.Contains("\"matcher\": \"Bash\"", settings);
        Assert.Contains("echo done", settings);
        Assert.Contains("\"model\": \"opus\"", settings);
        Assert.Equal(2, Hooks().GetProperty("Stop").GetArrayLength());
        Assert.Equal(1, StatusHookCount(Hooks().GetProperty("Stop")));
    }

    [Fact]
    public void Turning_status_hooks_off_removes_only_fleets_status_hooks()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(
            SettingsPath,
            """{"hooks":{"Stop":[{"hooks":[{"type":"command","command":"echo stopped"}]}]}}""");

        var writer = new ClaudeConfigWriter();

        writer.SyncWorktree(AgentServer(), _dir, [], [], [], "fleet.exe");
        writer.SyncWorktree(AgentServer(), _dir, [], [], []);

        var hooks = Hooks();

        Assert.Equal(0, StatusHookCount(hooks.GetProperty("Stop")));
        Assert.Contains("echo stopped", File.ReadAllText(SettingsPath));
        Assert.False(hooks.TryGetProperty("PreToolUse", out _));
        Assert.False(new ClaudeConfigWriter().Inspect(_dir, "fleet").StatusHooksInstalled);
    }

    [Fact]
    public void An_orchestration_folder_gets_both_the_dispatch_hook_and_the_status_hooks()
    {
        var plan = Plan() with { StatusHook = "fleet.exe" };

        new ClaudeConfigWriter().Sync(plan);

        var submit = Hooks().GetProperty("UserPromptSubmit");

        Assert.Equal(2, submit.GetArrayLength());
        Assert.Equal(1, StatusHookCount(submit));
        Assert.Contains("hook-dispatch", File.ReadAllText(SettingsPath));
        Assert.True(new ClaudeConfigWriter().Inspect(_dir, "fleet").StatusHooksInstalled);
    }

    [Fact]
    public void Approving_a_worktree_keeps_the_agents_own_settings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, """{"permissions":{"allow":["Bash(ls)"]},"model":"opus"}""");

        new ClaudeConfigWriter().SyncWorktree(AgentServer(), _dir, ["mcp__fleet__list_agents"], [], []);

        var settings = File.ReadAllText(SettingsPath);

        Assert.Contains("Bash(ls)", settings);
        Assert.Contains("\"model\": \"opus\"", settings);
        Assert.Contains("mcp__fleet__list_agents", settings);
    }

    [Fact]
    public void Broken_json_is_left_untouched_rather_than_clobbered()
    {
        File.WriteAllText(McpPath, "{ not json at all");

        var result = new ClaudeConfigWriter().Sync(Plan());

        Assert.False(result.Succeeded);
        Assert.Equal("{ not json at all", File.ReadAllText(McpPath));
    }

    [Fact]
    public void Inspect_reports_the_server_registered_enabled_and_allowed()
    {
        new ClaudeConfigWriter().Sync(Plan());

        var state = new ClaudeConfigWriter().Inspect(_dir, "fleet");

        Assert.True(state.ServerRegistered);
        Assert.True(state.ServerEnabled);
        Assert.Contains("mcp__fleet__list_agents", state.Allow);
    }

    [Fact]
    public void Inspect_of_an_unconfigured_folder_reports_nothing()
    {
        var state = new ClaudeConfigWriter().Inspect(_dir, "fleet");

        Assert.False(state.ServerRegistered);
        Assert.False(state.ServerEnabled);
    }

    private static IEnumerable<int> Occurrences(string text, string token)
    {
        var index = text.IndexOf(token, StringComparison.Ordinal);

        while (index >= 0)
        {
            yield return index;
            index = text.IndexOf(token, index + token.Length, StringComparison.Ordinal);
        }
    }
}
