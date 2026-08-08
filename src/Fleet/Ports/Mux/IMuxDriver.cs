namespace Fleet.Ports.Mux;

/// <summary>
/// Opaque by design. tmux pane ids look like "%12", WezTerm's are integers, and
/// the embedded driver will use its own scheme. A numeric type would bake one
/// multiplexer's numbering into the interface.
/// </summary>
public readonly record struct PaneId(string Value)
{
    public static readonly PaneId None = new(string.Empty);

    public bool IsNone => string.IsNullOrEmpty(Value);

    public override string ToString() => Value;
}

public sealed record Pane(
    PaneId Id,
    string WindowId,
    string SessionName,
    string Title,
    string Cwd,
    bool IsActive);

public sealed record SpawnOptions
{
    public string? Cwd { get; init; }

    /// <summary>
    /// tmux session / WezTerm workspace. Honoured only together with
    /// <see cref="NewWindow"/> — WezTerm rejects a workspace on a tab spawn.
    /// </summary>
    public string? SessionName { get; init; }

    public bool NewWindow { get; init; }

    /// <summary>The command line to run. Empty means the multiplexer's default shell.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];
}

public enum SplitDirection
{
    Right,
    Left,
    Top,
    Bottom,
}

public sealed record SplitOptions(PaneId Source, SplitDirection Direction)
{
    /// <summary>Size of the NEW pane as a percentage. Zero leaves the multiplexer's default.</summary>
    public int Percent { get; init; }

    public string? Cwd { get; init; }

    public IReadOnlyList<string> Args { get; init; } = [];
}

/// <summary>
/// What a driver can do, for progressive enhancement. Command code targets the
/// lowest common denominator and consults this only where a feature is optional.
/// <c>Popup</c> exists on tmux alone, which is why modals are drawn in-process.
/// </summary>
[Flags]
public enum MuxCaps
{
    None = 0,
    Split = 1 << 0,
    Zoom = 1 << 1,

    /// <summary>A client can leave while panes keep running.</summary>
    Detach = 1 << 2,

    /// <summary>Panes survive the multiplexer client exiting.</summary>
    Persist = 1 << 3,

    Popup = 1 << 4,
}

/// <summary>The multiplexer could not be reached: not installed, or not running.</summary>
public sealed class MuxUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Every multiplexer action goes through this.
///
/// Phase 1 defines the six verbs it needs. The full surface is in
/// docs/DESIGN.md and grows as commands require it — adding methods nothing
/// calls yet would only produce NotImplementedException landmines.
/// </summary>
public interface IMuxDriver
{
    string Name { get; }

    MuxCaps Caps { get; }

    /// <summary>The pane fleet is itself running in, or <see cref="PaneId.None"/>.</summary>
    PaneId CurrentPane { get; }

    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default);

    Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default);

    Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default);

    Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default);

    Task FocusPaneAsync(PaneId id, CancellationToken ct = default);
}
