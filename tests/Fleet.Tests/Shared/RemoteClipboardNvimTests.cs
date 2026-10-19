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
    public async Task A_local_pane_keeps_its_yank_behaviour()
    {
        var run = await RunAsync(remote: false, keys: "yy");

        Assert.Empty(run.Osc52Writes);
    }

    private static string Osc52(string text) => "\e]52;c;" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    private sealed record Run(string Output, IReadOnlyList<string> Osc52Writes, IReadOnlyList<string> Lines, string? ClipboardOption);

    private async Task<Run> RunAsync(bool remote, string keys, string? userConfig = null)
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
        return new Run(text, writes, lines, clipboard);
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
