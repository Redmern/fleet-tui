using System.Diagnostics;
using System.Text;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Shared;

public sealed class RemoteClipboardNvimTests : IDisposable
{
    private const string Text = "hello world\nsecond\n";

    private readonly string _dir = Directory.CreateTempSubdirectory("fleet-osc52-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [NvimFact]
    public async Task A_yank_in_a_remote_pane_is_written_to_the_terminal_as_osc_52()
    {
        var run = await RunAsync(remote: true, keys: "yyjyy");

        Assert.Equal(
            [Osc52("hello world\n"), Osc52("second\n")],
            run.Osc52Writes);
    }

    [NvimFact]
    public async Task A_visual_yank_and_a_plus_register_yank_are_written_as_osc_52()
    {
        var run = await RunAsync(remote: true, keys: "vey\"+yy");

        Assert.Equal([Osc52("hello"), Osc52("hello world\n")], run.Osc52Writes);
    }

    [NvimFact]
    public async Task A_paste_in_a_remote_pane_uses_the_yanked_text_and_never_asks_the_terminal()
    {
        var run = await RunAsync(remote: true, keys: "yyjp\"+p");

        Assert.Equal(["hello world", "second", "hello world", "hello world"], run.Lines);
        Assert.DoesNotContain("52;c;?", run.Output, StringComparison.Ordinal);
    }

    [NvimFact]
    public async Task Deletes_and_changes_in_a_remote_pane_stay_off_the_viewer_clipboard()
    {
        var run = await RunAsync(remote: true, keys: "ddxcwz");

        Assert.Empty(run.Osc52Writes);
    }

    [NvimFact]
    public async Task A_remote_pane_leaves_the_clipboard_option_alone()
    {
        var run = await RunAsync(remote: true, keys: "yy");

        Assert.Equal(string.Empty, run.ClipboardOption);
        Assert.Equal([Osc52("hello world\n")], run.Osc52Writes);
    }

    [NvimFact]
    public async Task A_clipboard_option_the_user_sets_keeps_working_through_the_provider_once()
    {
        var run = await RunAsync(remote: true, keys: "yy", userConfig: "vim.o.clipboard='unnamedplus'");

        Assert.Equal("unnamedplus", run.ClipboardOption);
        Assert.Equal([Osc52("hello world\n")], run.Osc52Writes);
    }

    [NvimFact]
    public async Task A_clipboard_the_user_config_sets_is_left_alone()
    {
        var run = await RunAsync(remote: true, keys: "yy", userConfig: "vim.g.clipboard=false");

        Assert.Empty(run.Osc52Writes);
    }

    [NvimFact]
    public async Task A_local_pane_with_a_clipboard_tool_keeps_its_yank_behaviour()
    {
        var run = await RunAsync(remote: false, keys: "yy\"+yy", provider: true);

        Assert.Empty(run.Osc52Writes);
        Assert.Equal("hello world\n", run.ProviderCopies);
    }

    [NvimFact]
    public async Task A_marked_pane_with_a_clipboard_tool_keeps_the_tool_and_also_sends_yanks_as_osc_52()
    {
        var run = await RunAsync(remote: true, keys: "yy\"+yy", provider: true);

        Assert.Equal([Osc52("hello world\n"), Osc52("hello world\n")], run.Osc52Writes);
        Assert.Equal("hello world\n", run.ProviderCopies);
    }

    [NvimFact]
    public async Task A_marked_pane_whose_clipboard_tool_is_a_fleet_shim_sends_a_clipboard_yank_only_through_the_shim()
    {
        var run = await RunAsync(remote: true, keys: "yy\"+yy", provider: true, shimmed: true);

        Assert.Equal([Osc52("hello world\n")], run.Osc52Writes);
        Assert.Equal("hello world\n", run.ProviderCopies);
    }

    [NvimFact]
    public async Task An_unmarked_pane_without_a_clipboard_tool_writes_a_yank_as_osc_52()
    {
        var run = await RunAsync(remote: false, keys: "yyjyy");

        Assert.Equal([Osc52("hello world\n"), Osc52("second\n")], run.Osc52Writes);
    }

    [NvimFact]
    public async Task An_unmarked_pane_without_a_clipboard_tool_copies_an_unnamedplus_yank_once()
    {
        var run = await RunAsync(remote: false, keys: "yy", userConfig: "vim.o.clipboard='unnamedplus'");

        Assert.Equal([Osc52("hello world\n")], run.Osc52Writes);
    }

    [NvimFact]
    public async Task An_unmarked_pane_without_a_clipboard_tool_leaves_a_user_clipboard_alone()
    {
        var run = await RunAsync(remote: false, keys: "yy", userConfig: "vim.g.clipboard=false");

        Assert.Empty(run.Osc52Writes);
    }

    [NvimFact]
    public async Task A_pane_with_a_clipboard_tool_also_sends_a_yank_to_a_bridged_viewer_once_one_attaches()
    {
        var bridged = Path.Combine(_dir, "bridged");
        await File.WriteAllTextAsync(bridged, string.Empty);

        var run = await RunAsync(remote: false, keys: "yy", provider: true, bridged: bridged);

        Assert.Equal([Osc52("hello world\n")], run.Osc52Writes);
    }

    [NvimFact]
    public async Task A_pane_with_a_clipboard_tool_keeps_a_yank_local_while_no_bridged_viewer_is_attached()
    {
        var run = await RunAsync(remote: false, keys: "yy", provider: true, bridged: Path.Combine(_dir, "bridged"));

        Assert.Empty(run.Osc52Writes);
    }

    [NvimFact]
    public async Task A_yank_is_traced_with_the_sha256_of_its_text()
    {
        var trace = Path.Combine(_dir, "trace.ndjson");

        await RunAsync(remote: false, keys: "yy", trace: trace);

        var line = Assert.Single(await File.ReadAllLinesAsync(trace));
        using var json = System.Text.Json.JsonDocument.Parse(line);
        Assert.Equal("nvim-yank", json.RootElement.GetProperty("hop").GetString());
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("hello world\n"))),
            json.RootElement.GetProperty("sha256").GetString());
        Assert.Equal(12, json.RootElement.GetProperty("bytes").GetInt32());
    }

    private static string Osc52(string text) => "\e]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private sealed record Run(
        string Output, IReadOnlyList<string> Osc52Writes, IReadOnlyList<string> Lines, string? ClipboardOption, string? ProviderCopies);

    private async Task<Run> RunAsync(
        bool remote, string keys, string? userConfig = null, bool provider = false, string? bridged = null, string? trace = null,
        bool shimmed = false)
    {
        var file = Path.Combine(_dir, "f.txt");
        var result = Path.Combine(_dir, "result.txt");
        await File.WriteAllTextAsync(file, Text);
        await File.WriteAllTextAsync(Path.Combine(_dir, "boot.lua"), AgentHarness.RemoteClipboard);
        await File.WriteAllTextAsync(Path.Combine(_dir, "user.lua"), userConfig ?? string.Empty);
        await File.WriteAllTextAsync(
            Path.Combine(_dir, "drive.lua"),
            "vim.api.nvim_create_autocmd('UIEnter',{once=true,callback=function() vim.schedule(function() "
            + $"vim.api.nvim_feedkeys([[{keys}]],'nx',false) "
            + "vim.cmd('silent! w! result.txt') vim.fn.writefile({vim.o.clipboard},'clipboard.txt') "
            + "vim.cmd('qa!') end) end})");

        var start = new ProcessStartInfo("script")
        {
            WorkingDirectory = _dir,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList =
            {
                "-q", "-e", "-c",
                "nvim --clean -u user.lua -c 'luafile boot.lua' -c 'luafile drive.lua' f.txt",
                "/dev/null",
            },
        };
        start.Environment[AgentHarness.RemotePaneVariable] = remote ? "1" : string.Empty;
        start.Environment.Remove("DISPLAY");
        start.Environment.Remove("WAYLAND_DISPLAY");
        start.Environment.Remove("TMUX");
        start.Environment.Remove(AgentHarness.BridgedFileVariable);
        start.Environment.Remove(AgentHarness.ClipboardTraceVariable);
        start.Environment.Remove(AgentHarness.ShimDirVariable);
        var copies = Path.Combine(_dir, "provider.txt");
        if (provider && !OperatingSystem.IsWindows())
        {
            var tools = Directory.CreateDirectory(Path.Combine(_dir, "tools")).FullName;
            WriteTool(Path.Combine(tools, "wl-copy"), $"cat >> '{copies}'");
            WriteTool(Path.Combine(tools, "wl-paste"), "true");
            start.Environment["WAYLAND_DISPLAY"] = "wayland-test";
            start.Environment["PATH"] = tools + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            if (shimmed)
            {
                start.Environment[AgentHarness.ShimDirVariable] = tools;
            }
        }

        if (bridged is not null)
        {
            start.Environment[AgentHarness.BridgedFileVariable] = bridged;
        }

        if (trace is not null)
        {
            start.Environment[AgentHarness.ClipboardTraceVariable] = trace;
        }
        start.Environment["TERM"] = "xterm-256color";

        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var text = await output;

        var writes = text.Split("\e]52;").Skip(1)
            .Select(chunk => "\e]52;" + chunk[..chunk.IndexOfAny(['\e', '\a'])])
            .ToList();
        var lines = File.Exists(result) ? await File.ReadAllLinesAsync(result) : [];
        var option = Path.Combine(_dir, "clipboard.txt");
        var clipboard = File.Exists(option) ? (await File.ReadAllLinesAsync(option)).FirstOrDefault() : null;
        return new Run(text, writes, lines, clipboard, File.Exists(copies) ? await File.ReadAllTextAsync(copies) : null);
    }

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static void WriteTool(string path, string body)
    {
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private sealed class NvimFactAttribute : FactAttribute
    {
        public NvimFactAttribute()
        {
            if (!OperatingSystem.IsLinux() || !OnPath("nvim") || !OnPath("script"))
            {
                Skip = "needs Linux with nvim and util-linux script on PATH";
            }
        }

        private static bool OnPath(string tool) =>
            (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Any(dir => File.Exists(Path.Combine(dir, tool)));
    }
}
