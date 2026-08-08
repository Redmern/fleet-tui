using System.Text.Json;
using Fleet.Ports.Mux;

namespace Fleet.Platform.Mux.WezTerm;

public sealed class WezTermDriver(WezTermCli? cli = null) : IMuxDriver
{
    private readonly WezTermCli _cli = cli ?? new WezTermCli();

    public string Name => "wezterm";

    public MuxCaps Caps => MuxCaps.Split | MuxCaps.Zoom | MuxCaps.Persist;

    /// <summary>
    /// The pane fleet is running in, read from WEZTERM_PANE.
    ///
    /// Absent is <see cref="PaneId.None"/> and NOT pane 0: WezTerm numbers panes
    /// from zero, so the first pane of a fresh window really is 0. Treating 0 as
    /// "no pane" breaks hooks and doctor, but only on a freshly started terminal —
    /// which is exactly when it is least expected.
    /// </summary>
    public PaneId CurrentPane
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("WEZTERM_PANE");
            return string.IsNullOrWhiteSpace(raw) ? PaneId.None : new PaneId(raw.Trim());
        }
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

        var rows = JsonSerializer.Deserialize(json, WezTermJsonContext.Default.WezTermPaneJsonArray)
                   ?? [];

        return rows.Select(r => new Pane(
            Id: new PaneId(r.PaneId.ToString()),

            // WezTerm's "tab" is the unit fleet treats as a window: one project per
            // tab, split into a harness pane and a dashboard pane.
            WindowId: r.TabId.ToString(),
            SessionName: r.Workspace,
            Title: string.IsNullOrEmpty(r.TabTitle) ? r.Title : r.TabTitle,
            Cwd: CwdUrl.Normalize(r.Cwd),
            IsActive: r.IsActive)).ToList();
    }

    public async Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
    {
        var args = new List<string> { "spawn" };

        if (!string.IsNullOrEmpty(options.Cwd))
        {
            args.Add("--cwd");
            args.Add(options.Cwd);
        }

        if (options.NewWindow)
        {
            args.Add("--new-window");

            // wezterm rejects --workspace combined with a tab spawn, so it is only
            // passed alongside --new-window.
            if (!string.IsNullOrEmpty(options.SessionName))
            {
                args.Add("--workspace");
                args.Add(options.SessionName);
            }
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
