using System.Text.Json;
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Enums;
using Fleet.Ports.Mux.Exceptions;
using Fleet.Ports.Mux.Models;

namespace Fleet.Platform.Mux.WezTerm;

public sealed class WezTermDriver(WezTermCli? cli = null) : IMuxDriver
{
    private readonly WezTermCli _cli = cli ?? new WezTermCli();

    public string Name => "wezterm";

    public MuxCaps Caps => MuxCaps.Split | MuxCaps.Zoom | MuxCaps.Persist;

    public PaneId CurrentPane
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("WEZTERM_PANE");
            return string.IsNullOrWhiteSpace(raw) ? PaneId.None : new PaneId(raw.Trim());
        }
    }

    public static IReadOnlyList<Pane> ParsePanes(string json)
    {
        var rows = JsonSerializer.Deserialize(json, WezTermJsonContext.Default.WezTermPaneJsonArray)
                   ?? [];

        return rows.Select(r => new Pane(
            Id: new PaneId(r.PaneId.ToString()),
            WindowId: r.WindowId.ToString(),
            TabId: r.TabId.ToString(),
            SessionName: r.Workspace,
            Title: string.IsNullOrEmpty(r.TabTitle) ? r.Title : r.TabTitle,
            Cwd: CwdUrl.Normalize(r.Cwd),
            IsActive: r.IsActive)).ToList();
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            await _cli.RunAsync(["list", "--format", "json"], ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is MuxUnavailableException or TimeoutException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default)
    {
        var json = await _cli.RunAsync(["list", "--format", "json"], ct).ConfigureAwait(false);
        return ParsePanes(json);
    }

    public async Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
    {
        var args = new List<string> { "spawn" };

        if (!string.IsNullOrEmpty(options.Cwd))
        {
            args.Add("--cwd");
            args.Add(options.Cwd);
        }

        if (options.NewWindow || !string.IsNullOrEmpty(options.Workspace))
        {
            args.Add("--new-window");
        }

        if (!string.IsNullOrEmpty(options.Workspace))
        {
            args.Add("--workspace");
            args.Add(options.Workspace);
        }

        if (options.Args.Count > 0)
        {
            args.Add("--");
            args.AddRange(options.Args);
        }

        return ParsePaneId(await _cli.RunAsync(args, ct).ConfigureAwait(false));
    }

    public async Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "split-pane",
            "--pane-id",
            options.Source.Value,
            DirectionFlag(options.Direction),
        };

        if (options.Percent > 0)
        {
            args.Add("--percent");
            args.Add(options.Percent.ToString());
        }

        if (!string.IsNullOrEmpty(options.Cwd))
        {
            args.Add("--cwd");
            args.Add(options.Cwd);
        }

        if (options.Args.Count > 0)
        {
            args.Add("--");
            args.AddRange(options.Args);
        }

        return ParsePaneId(await _cli.RunAsync(args, ct).ConfigureAwait(false));
    }

    public async Task MovePaneAsync(
        PaneId id, MovePaneOptions options, CancellationToken ct = default)
    {
        var args = new List<string> { "move-pane-to-new-tab", "--pane-id", id.Value };

        if (options.NewWindow || !string.IsNullOrEmpty(options.Workspace))
        {
            args.Add("--new-window");
        }

        if (!string.IsNullOrEmpty(options.Workspace))
        {
            args.Add("--workspace");
            args.Add(options.Workspace);
        }

        if (!string.IsNullOrEmpty(options.WindowId))
        {
            args.Add("--window-id");
            args.Add(options.WindowId);
        }

        await _cli.RunAsync(args, ct).ConfigureAwait(false);
    }

    public async Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
        => await _cli.RunAsync(["set-tab-title", "--pane-id", id.Value, title], ct)
            .ConfigureAwait(false);

    public async Task FocusPaneAsync(PaneId id, CancellationToken ct = default)
        => await _cli.RunAsync(["activate-pane", "--pane-id", id.Value], ct).ConfigureAwait(false);

    private static string DirectionFlag(SplitDirection d) => d switch
    {
        SplitDirection.Right => "--right",
        SplitDirection.Left => "--left",
        SplitDirection.Top => "--top",
        SplitDirection.Bottom => "--bottom",
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    private static PaneId ParsePaneId(string output)
    {
        var s = output.Trim();

        if (!int.TryParse(s, out _))
        {
            throw new MuxUnavailableException($"expected a pane id, got \"{s}\"");
        }

        return new PaneId(s);
    }
}
