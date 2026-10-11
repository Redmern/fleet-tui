using System.Diagnostics;
using Fleet.Platform.Claude;
using Fleet.Shared.Orchestrations;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Platform.Claude;

public sealed class ClaudeModelRouterTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private static readonly ModelRoutingRequest Request = new("fix the login timeout", "bugfix", false, "backend");

    public ClaudeModelRouterTests() => Directory.CreateDirectory(_dir);

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

    private string FakeClaude(string body)
    {
        var path = Path.Combine(_dir, "claude");
        var argsFile = Path.Combine(_dir, "args");

        File.WriteAllText(path, $"#!/bin/sh\nprintf '%s\\n' \"$@\" > '{argsFile}'\n{body}\n");

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private string[] ArgsSeen() => File.ReadAllLines(Path.Combine(_dir, "args"));

    [Fact]
    public async Task A_valid_reply_is_returned_as_the_routed_model()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var router = new ClaudeModelRouter(FakeClaude("echo 'model=opus effort=high'"));

        Assert.Equal(new RoleModel("opus", "high"), await router.ChooseAsync(Request));
    }

    [Fact]
    public async Task Claude_is_asked_in_print_mode_on_haiku_with_text_output_and_the_routing_prompt()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await new ClaudeModelRouter(FakeClaude("echo 'model=opus effort=high'")).ChooseAsync(Request);

        var args = string.Join("\n", ArgsSeen());

        Assert.StartsWith("-p\n" + ModelRouting.BuildPrompt(Request) + "\n", args);
        Assert.EndsWith("\n--model\nhaiku\n--output-format\ntext", args);
    }

    [Fact]
    public async Task A_non_zero_exit_yields_nothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var router = new ClaudeModelRouter(FakeClaude("echo 'model=opus effort=high'\nexit 3"));

        Assert.Null(await router.ChooseAsync(Request));
    }

    [Fact]
    public async Task An_invalid_reply_yields_nothing()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var router = new ClaudeModelRouter(FakeClaude("echo 'model=gpt effort=high'"));

        Assert.Null(await router.ChooseAsync(Request));
    }

    [Fact]
    public async Task A_router_slower_than_its_budget_yields_nothing_without_waiting_for_it()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var router = new ClaudeModelRouter(
            FakeClaude("sleep 10\necho 'model=opus effort=high'"), TimeSpan.FromMilliseconds(300));

        var clock = Stopwatch.StartNew();
        var chosen = await router.ChooseAsync(Request);

        Assert.Null(chosen);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task A_missing_executable_yields_nothing()
    {
        var router = new ClaudeModelRouter(Path.Combine(_dir, "no-such-claude"));

        Assert.Null(await router.ChooseAsync(Request));
    }
}
