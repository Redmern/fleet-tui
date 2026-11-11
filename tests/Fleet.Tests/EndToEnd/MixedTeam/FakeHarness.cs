using System.Text.RegularExpressions;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Harness;
using Fleet.Shared.Orchestrations;

namespace Fleet.Tests.EndToEnd.MixedTeam;

// A scripted stand-in for a harness TUI in a pane. It does what fleet relies on and nothing more:
// - it starts the fleet MCP server named in its own project-local config, which writes the ready marker (the real
//   `fleet mcp --caller x` does that in McpWiring.ReadySignal);
// - it shows typed text on its screen and submits the input line on Enter;
// - a TUI without a ready hook (opencode, codex, pi) loses the first thing typed into it, as opencode 1.4.6 did
//   in the u7 live proof.
// The "model turn" is played by the test: it reads Submitted and calls fleet tools as Caller.
internal sealed partial class FakeHarness
{
    private static readonly IReadOnlyDictionary<string, string> McpConfigFiles = new Dictionary<string, string>
    {
        [HarnessRegistry.OpencodeName] = "opencode.json",
        [HarnessRegistry.CodexName] = Path.Combine(".codex", "config.toml"),
        [HarnessRegistry.PiName] = Path.Combine(".pi", "mcp.json"),
    };

    private readonly object _gate = new();
    private readonly FakeMuxDriver _mux;
    private readonly List<string> _screen = [];
    private readonly List<string> _submitted = [];
    private string _input = string.Empty;
    private bool _losesNextTyping;
    private int _lost;

    private FakeHarness(FakeMuxDriver mux, PaneId pane, HarnessProfile profile, string folder)
    {
        _mux = mux;
        Pane = pane;
        Profile = profile;
        Folder = folder;
        _losesNextTyping = profile.InputRetry > TimeSpan.Zero;
    }

    public PaneId Pane { get; }

    public HarnessProfile Profile { get; }

    public string Folder { get; }

    public Task Started { get; private set; } = Task.CompletedTask;

    public string? Caller { get; private set; }

    public string? CallerProject { get; private set; }

    public IReadOnlyList<string> Submitted
    {
        get
        {
            lock (_gate)
            {
                return [.. _submitted];
            }
        }
    }

    public int LostTypings
    {
        get
        {
            lock (_gate)
            {
                return _lost;
            }
        }
    }

    public string Instruction => File.ReadAllText(Path.Combine(Folder, ".fleet", "instruction.md"));

    public static FakeHarness Start(FakeMuxDriver mux, PaneId pane, HarnessProfile profile, string folder)
    {
        var harness = new FakeHarness(mux, pane, profile, folder);
        harness.Render();
        harness.Started = McpConfigFiles.TryGetValue(profile.Name, out var config)
            ? Task.Run(() => harness.StartMcpServerAsync(Path.Combine(folder, config)))
            : harness.Ready();

        return harness;
    }

    public void Key(string text)
    {
        lock (_gate)
        {
            if (text == "\r")
            {
                if (_input.Length > 0)
                {
                    _submitted.Add(_input);
                    _screen.Add($"user: {_input}");
                    _input = string.Empty;
                }
            }
            else if (_losesNextTyping)
            {
                _losesNextTyping = false;
                _lost++;
            }
            else
            {
                _input += text;
            }
        }

        Render();
    }

    public void Says(string line)
    {
        lock (_gate)
        {
            _screen.Add($"{Profile.Name}: {line}");
        }

        Render();
    }

    private async Task StartMcpServerAsync(string config)
    {
        for (var i = 0; i < 500 && Caller is null; i++)
        {
            var text = ReadOrEmpty(config);
            var caller = CallerArgument().Match(text);
            var project = ProjectArgument().Match(text);

            if (caller.Success && project.Success)
            {
                CallerProject = project.Groups[1].Value;
                Caller = caller.Groups[1].Value;
                break;
            }

            await Task.Delay(20).ConfigureAwait(false);
        }

        if (Caller is not null)
        {
            await Ready().ConfigureAwait(false);
        }
    }

    // fleet may still be writing the file (it is written after the pane is spawned).
    private static string ReadOrEmpty(string config)
    {
        try
        {
            return File.Exists(config) ? File.ReadAllText(config) : string.Empty;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private Task Ready()
    {
        var marker = OrchestrationPaths.ReadyMarker(Folder);
        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, string.Empty);

        return Task.CompletedTask;
    }

    private void Render()
    {
        string text;

        lock (_gate)
        {
            text = string.Join('\n', [$"{Profile.Name} in {Folder}", .. _screen, $"> {_input}"]);
        }

        _mux.SetText(Pane, text);
    }

    [GeneratedRegex("\"--caller\"\\s*,\\s*\"([^\"]+)\"")]
    private static partial Regex CallerArgument();

    [GeneratedRegex("\"--project\"\\s*,\\s*\"([^\"]+)\"")]
    private static partial Regex ProjectArgument();
}
