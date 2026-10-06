# fleet — design

**Status:** design complete. One decision is deliberately deferred (agent state
storage, settled at M2) and three items remain to verify before the code they
gate is written.
**Date:** 2026-08-08

A TUI orchestration tool for AI work across projects that span one or more
repositories. Neovim is the editor. Claude Code is the agent. A third attempt at
the idea behind [`Redmern/fleet`](https://github.com/Redmern/fleet) (tmux, Linux,
bash + Python) and `fleet-win` (WezTerm, Windows, Go) — this time built once, for
both, with the multiplexer behind an interface.

## Goal

Get the core feature right before the surface area grows. `fleet-win` reached 48
commands; this one starts from the smallest thing that actually orchestrates
agents and grows from there.

## Deployment targets

All four must work:

| Target | Notes |
|---|---|
| Windows, native | Real `nvim.exe`, Windows git, no WSL |
| Windows, headless | Over SSH into Windows, no GUI |
| Linux, native | Desktop or VM |
| Linux, headless | Over SSH, detach and reattach expected |

## Decision: the multiplexer is swappable

No single multiplexer covers that matrix.

| Candidate | Gap |
|---|---|
| tmux | No Win32 build. Needs WSL, MSYS2, or Cygwin — excluded by "native Windows". |
| WezTerm | Native on both, but its CLI needs a running GUI. Excluded by headless. |
| Zellij | No native Windows support. |

So fleet does not pick one. Every multiplexer action goes through an
`IMuxDriver` interface with swappable implementations:

| Driver | Role | Covers | Cost |
|---|---|---|---|
| `wezterm` | **base** | Windows native (GUI), Linux native (GUI) | Low — `fleet-win` has a working reference |
| `tmux` | **fallback** | SSH, headless Linux, WSL, MSYS2, and anywhere WezTerm is absent | Low — richest CLI of the three |
| `embedded` | last resort | Headless Windows, and anywhere with no mux at all | High — this is writing a multiplexer |
| `fake` | tests | — | Low, and it pays for itself immediately |

WezTerm is the daily driver on both operating systems; the machine runs Linux
with a GUI, so there is no platform split. tmux exists so fleet still works with
no WezTerm — over SSH, on a headless box, or on a machine that never installed
it.

The `embedded` driver is an attach-model daemon: it owns the PTYs, outlives its
clients, and attaches one pane at a time, fullscreen. Deliberately no tiling —
attach/detach and tiling are separable, and tiling is the expensive half.

**Revised 2026-09-24:** each pane runs through a libghostty-vt terminal emulator
inside the daemon, and clients receive frames the daemon renders from it. The
earlier position — no emulator, because an emulator is what mangles Neovim
(truecolor, undercurl, SGR mouse, bracketed paste, focus events, kitty keyboard
protocol) — was true of the emulators available then, not of Ghostty's. The
Phase 0 spike showed nvim and claude intact through it. See *The `embedded`
driver, Phase 0 spike* and *Remote attach* below.

**Revised again 2026-09-24:** tiling *within a workspace* is now in scope. Each
project is a workspace with its split layout (claude, dashboard, agents,
sub-orchestrator browsers), and a client shows one workspace at a time. The
emulator per pane is what makes that affordable: the daemon composites several
pane grids into one frame. See *Instant project switching on `embedded`*.

**Build order:** `fake`, then `wezterm`, then `tmux`, then `embedded`. WezTerm
first because it is the daily driver and `fleet-win` supplies a debugged
reference, so it is the shortest path to a fleet worth using. tmux second, early
enough that whatever it reveals about the interface is still cheap to fix.
`embedded` last, and only for headless Windows.

## The interface

Vocabulary first, because the three multiplexers disagree on names:

| fleet | tmux | WezTerm | embedded |
|---|---|---|---|
| Session | session | workspace | session |
| Window | window | tab | window |
| Pane | pane | pane | pane |

```csharp
// Opaque by design: tmux "%12", wezterm "7", embedded "p3".
public readonly record struct PaneId(string Value);

[Flags]
public enum MuxCaps
{
    None    = 0,
    Split   = 1 << 0,
    Zoom    = 1 << 1,
    Detach  = 1 << 2,  // client can leave; panes keep running
    Persist = 1 << 3,  // panes survive the mux client exiting
    Popup   = 1 << 4,  // tmux display-popup; nothing else has it
}

public interface IMuxDriver
{
    string Name { get; }
    MuxCaps Caps { get; }
    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default);
    Task<Pane?> GetPaneAsync(PaneId id, CancellationToken ct = default);

    Task<IReadOnlyList<string>> ListSessionsAsync(CancellationToken ct = default);
    Task<bool> SessionExistsAsync(string name, CancellationToken ct = default);
    Task RenameSessionAsync(string oldName, string newName, CancellationToken ct = default);

    Task<PaneId> SpawnAsync(SpawnOptions opts, CancellationToken ct = default);
    Task<PaneId> SplitAsync(SplitOptions opts, CancellationToken ct = default);
    Task KillPaneAsync(PaneId id, CancellationToken ct = default);
    Task KillWindowAsync(PaneId id, CancellationToken ct = default);
    Task KillWindowAndWaitAsync(PaneId id, TimeSpan timeout, CancellationToken ct = default);

    Task SendTextAsync(PaneId id, string text, bool literal, CancellationToken ct = default);
    Task<string> CaptureTextAsync(PaneId id, CancellationToken ct = default);

    Task FocusPaneAsync(PaneId id, CancellationToken ct = default);
    Task FocusWindowAsync(PaneId id, CancellationToken ct = default);
    Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default);
    Task ZoomAsync(PaneId id, bool on, CancellationToken ct = default);

    Task AttachAsync(PaneId id, CancellationToken ct = default);
    PaneId? CurrentPane { get; }
}
```

Three corrections against `fleet-win`'s `internal/mux`, each forced by tmux:

- `PaneId` wraps a string, not an `int`. tmux ids look like `%12`. An `int` bakes
  WezTerm's numbering into the interface.
- `Attach` is a first-class verb. tmux: `tmux attach -t`. WezTerm: activate the
  tab, since the GUI is already in front of you. Embedded: begin raw
  passthrough. It genuinely unifies.
- `Caps` for progressive enhancement. Command code targets the lowest common
  denominator and consults `Caps` only where a feature is optional. `Popup`
  exists on tmux alone, so nothing depends on it — modals are drawn in-process,
  the way `fleet-win` had to.

`literal` on `SendTextAsync` is not cosmetic: the permission-mode cycle sends
CSI Z (Shift+Tab), and bracketed paste would wrap it so the agent never sees a
keypress. Prose wants paste mode; control sequences want literal.

Preserved from `fleet-win`: the fail-silent contract lives in the driver layer,
enforced once instead of at every call site. An unreachable mux surfaces as an
empty result rather than an exception escaping into command code.

### Driver selection

Two separate decisions, often conflated:

**Adopt** — if fleet starts inside an existing multiplexer it must use that one,
whatever the preference order says. Otherwise you sit in a tmux pane while fleet
spawns WezTerm tabs. This is correctness, not taste.

**Launch** — only when nothing is around fleet does preference apply.

```
FLEET_MUX set          -> that                     (override)
TMUX set               -> tmux                     (adopt)
WEZTERM_PANE set       -> wezterm                  (adopt)
otherwise:                                         (launch)
    this build has libghostty-vt       -> embedded (base, since 0.6.0)
    wezterm present AND GUI reachable  -> wezterm
    tmux present                       -> tmux     (fallback)
    otherwise                          -> embedded (last resort)
```

**2026-09-29: embedded became the base for a plain terminal.** Running `fleet`
in a plain PowerShell or shell now opens the project right there, under
fleetd, instead of handing it to a WezTerm window. Adopting is unchanged:
inside WezTerm (the keybindings spawn fleet in a WezTerm tab, so they stay on
WezTerm) or tmux, fleet uses that multiplexer. `FLEET_MUX=wezterm` sends a
plain terminal to WezTerm as before. *This build has libghostty-vt* is
`GhosttyNative.Available()`, so a build without it falls through to the old
order. One consequence: a Claude session started by hand in a plain terminal,
with fleet's MCP server, now opens agents in fleetd (see them with
`fleet attach`) rather than in WezTerm.

*GUI reachable* — Linux: `DISPLAY` or `WAYLAND_DISPLAY` set. Windows:
`SSH_CONNECTION` unset.

No OS sniffing anywhere. Two behaviours fall out of this for free:

- SSH into the Linux box drops to tmux automatically, because neither display
  variable survives the hop.
- fleet running inside tmux under MSYS2 on Windows adopts tmux, with no special
  case.

Because WezTerm is the base on both operating systems, `wezterm/fleet.lua` — tab
glyphs, toast notifications, keybindings — is load-bearing on Linux too, not
just Windows. One Lua config, both platforms.

**Settled (2026-08-08):** `wezterm-mux-server` does *not* remove the need for
`embedded`. Control works headless — `wezterm cli --prefer-mux` talks to a
background mux server — but the only frontend that renders panes is the GUI, so
on a headless box you could drive panes and never see them. Details in the
verification log.

Worth keeping, though: the `wezterm` driver's control path does not require a
GUI. Against a `wezterm-mux-server --daemonize`, an agent spawned from a Claude
Code hook or from cron with no GUI running still works; you attach and view it
later.

## Stack

**C# on .NET 10, NativeAOT.** Chosen for maintainer fluency. Speed is not a
factor either way — nothing here is CPU-bound; it is process spawning, byte
copying, and JSON. The real costs are distribution and library depth, and they
are addressed below rather than avoided.

| Concern | Choice | Note |
|---|---|---|
| Runtime | .NET 10, NativeAOT | ~10 ms cold start. Matters: Claude Code hooks invoke `fleet` per tool use, and JIT startup at ~60 ms would be felt |
| Build | GitHub Actions matrix, `windows-latest` + `ubuntu-latest` | **NativeAOT cannot cross-compile between operating systems.** Each target builds on its own runner |
| TUI | Terminal.Gui v2 (`2.4.17`) | AOT verified — see below |
| Rich CLI output | Spectre.Console | Non-interactive commands. `Terminal.Gui.Interop.Spectre` bridges the two |
| JSON | `System.Text.Json` with source generators | Reflection-based serialization is not AOT-safe |
| Config | TOML via Tomlyn, or plain JSON | One `harness.d/<name>.toml` per agent CLI. JSON drops a dependency and an AOT risk; decide when the harness format is designed |
| IPC | `NamedPipeServerStream` / `UnixDomainSocketEndPoint` | Both BCL, no P/Invoke. C# is genuinely better than Go here |
| Signals | `PosixSignalRegistration` | BCL. Covers `SIGWINCH` |
| PTY | `Porta.Pty` — pending an AOT spike | See below |
| git | Shell out to `git` | No library. Same choice `fleet-win` made |

### PTY

The `embedded` driver needs a pseudo-terminal on both platforms. The hard part
is Unix, not Windows: **`fork` is unsafe in the .NET runtime**, and `posix_spawn`
cannot issue the `ioctl(TIOCSCTTY)` that gives the child a controlling terminal
in the window between fork and exec. Any solution has to get the fork out of
managed code.

`Porta.Pty` (MIT, `tomlm/Porta.Pty`, v1.0.7, ~92k downloads) does exactly that.
It ships a native C shim, `libporta_pty`, that performs `forkpty()` + `execvp()`
in native code specifically so no managed .NET code runs in the forked child.
Windows uses ConPTY; Linux and macOS use POSIX PTY.

```
PtyProvider.SpawnAsync(PtyOptions, CancellationToken) -> IPtyConnection
IPtyConnection { ReaderStream, WriterStream, Resize(cols, rows),
                 ProcessExited, ExitCode }
```

Not yet cleared, and a spike must settle it before the `embedded` driver starts:

| Signal | Reading |
|---|---|
| Targets netstandard2.0, depends on `Vanara.PInvoke.Kernel32` | AOT behaviour of that P/Invoke layer is untested |
| No NativeAOT or trimming statement anywhere | Unknown, not known-good |
| 24 stars, one maintainer, last push 2026-02-20 | Bus factor 1, on the riskiest component |

Fallbacks if the spike fails:

- `RoyalApps.RoyalTerminal.Terminal.Pty.{Platform,Unix,Windows}` — MIT, from
  RoyalApps, pushed 2026-08-03, platform-split. Newer and more actively
  developed, but v0.5.0 and ~4k downloads.
- `Microsoft.Windows.Console.ConPTY` (Microsoft, Windows-only, preview) paired
  with a hand-written Unix side and a small helper binary for the controlling
  terminal.
- `Quick.PtyNet` is a fork of the defunct `Pty.Net`. 6 stars, no declared
  license. Not viable.

### Remaining P/Invoke

Even with `Porta.Pty`, terminal *mode* handling stays ours. Confined to
`Fleet.Pty`.

*Windows raw mode* — `GetConsoleMode` / `SetConsoleMode`. Set
`ENABLE_VIRTUAL_TERMINAL_INPUT` and `ENABLE_VIRTUAL_TERMINAL_PROCESSING`; clear
`ENABLE_LINE_INPUT`, `ENABLE_ECHO_INPUT`, `ENABLE_PROCESSED_INPUT`.

*Unix raw mode* — `tcgetattr`, `cfmakeraw`, `tcsetattr`.

*Resize* — `Porta.Pty`'s `Resize` covers the child side. Reading the client's own
size on `SIGWINCH` uses `PosixSignalRegistration` plus `ioctl(TIOCGWINSZ)`.

### Relationship to fleet-win

`fleet-win` is Go and shares no code with this. It is **reference material**:
a working WezTerm driver, a hook reporter with correct subagent filtering, a
worktree layout, a harness format, and a set of debugged traps worth carrying
over by hand —

- `activate-tab` takes `--tab-id`, not `--pane-id`; passing the latter alone
  makes WezTerm reject the call outright and silently breaks every jump.
- WezTerm reports cwd as a `file://` URL — `file:///C:/repos/x` on Windows, where
  the leading slash must be dropped, and `file:///home/red/x` on Unix, where it
  must be kept.
- WezTerm numbers panes from 0, so a "no pane" sentinel cannot be 0.
- Killing a tab returns before its processes exit. On Windows a process holding
  the worktree as its cwd locks it, so an immediate `git worktree remove` fails
  with "Permission denied". Wait for the panes to actually disappear, then wait
  a little longer.

`wezterm/fleet.lua` and `nvim/fleet.lua` are the only files that port across
directly, being Lua. Both need review against the current Neovim config.

## Projects, repos, and agents

Driver-independent. This is the domain model; nothing here knows what a
multiplexer is.

### Project

A project is a **name pointing at a root folder whose children are
repositories** — not a repository itself. Multi-repo is the default case rather
than a feature bolted on later.

`Environment.SpecialFolder.ApplicationData` resolves to `%APPDATA%` on Windows
and honours `XDG_CONFIG_HOME` on Linux, so one BCL call covers both platforms.

### Repo layouts — detected, not configured

| Layout | Shape | Worktree policy |
|---|---|---|
| Plain | `<root>/<repo>/.git` | Edited in place. fleet imposes no worktree |
| Bare container | `<root>/<repo>` is bare | Worktrees are its children |
| Worktree container | `<root>/<repo>/<branch>/.git` | Sibling checkouts |

Discovery: a child of the root counts as a repo if it has `.git`, **or if any of
its own children does**. That second clause is what makes both container layouts
work.

Branch names are slugged for use as directory names and window titles —
`feature/foo` becomes `feature_foo`.

### Repo resolution

Exact case-insensitive match first, then unique substring, so `techweb` resolves
`AZ-ALZ-TechWeb-Backend-V2`. **Ambiguity is an error, not a pick.** The bash
original silently took the last match; that spawns an agent against the wrong
repository and is expensive to notice.

### Agent identity

An agent is `(repo, branch, harness)` bound to a worktree directory.

**Identity is the worktree path, never the pane id.** Pane ids are transient and
driver-specific — a `PaneId` from the `wezterm` driver is meaningless to `tmux`.
Keying on the path is what makes restore-after-terminal-close possible, and it is
why swapping drivers does not touch agent state.

**One agent is bound to exactly one repository.** Work spanning several repos in
a project is several agents plus the orchestrator coordinating them. Accepted
consequence: no single agent ever sees the whole cross-repo change. The
alternative — a task owning several worktrees at once — was considered and
rejected as too much state for the value.

### Worktree planning

Planning is a pure function of `(repoBase, branch)` and does no more than stat
the filesystem. It yields the target directory, whether it must still be
created, and the anchor checkout that `git worktree add` runs from. A plain repo
plans to itself with nothing to create.

### Four traps to transcribe with tests

These are already-paid-for bugs in `fleet-win`'s `gitx`. Port them deliberately.

**1. Base-ref selection.** Prefer the *local* branch when it is ahead of or
diverged from origin; use origin only when origin is ahead or equal. Cutting a
new agent's branch from a stale `origin/<base>` silently reverts unpushed local
merges, and every agent spawned afterwards rebuilds work that already exists.

**2. Default branch.** `origin/HEAD` → the anchor's own `HEAD` → `"main"`.
Jumping straight from `origin/HEAD` to a hardcoded `main` is wrong for any repo
with no remote, and for any repo whose trunk is still `master`.

**3. Dirty check.** Ignore `.fleet/` — fleet writes the ready marker, the
generated launch script and the agent settings there, all untracked, so counting
them makes every fleet-spawned worktree permanently dirty and impossible to reap
without a force flag. And **verify `.git` exists at the directory first**:
`git status` walks *up* until it finds a repository, so a half-removed worktree
reports whatever encloses it. A teardown decision made on another repo's
cleanliness deletes the wrong thing.

**4. Teardown order.** Check dirty (ignoring `.fleet/`) → `git worktree remove
--force` → `git worktree prune`. The force flag is needed because git's own
check does *not* ignore `.fleet/` and would refuse on files fleet itself wrote.
Never delete `.fleet/` first: a removal that then fails — a live agent still
holding the directory on Windows — leaves the worktree in place with its
done-marker destroyed, silently invisible to reaping. Removal runs from the main
worktree, resolved via `git rev-parse --git-common-dir`, because the worktree's
parent directory is the container and not a repository at all.

### State on disk

Two kinds, both under the config directory:

- **Projects** — `projects/<name>.json`. Name and root, home contracted to `~`
  so a project is portable between machines.
- **Sessions** — `sessions/<session>.json`. The project root, the saved agent
  records, and which pane holds the dashboard.

An agent record carries enough to respawn it: worktree directory, repo, branch,
whether the repo was bare, base ref, harness.

JSON with a version field and a source-generated `JsonSerializerContext`, not
`fleet-win`'s hand-parsed two-line YAML and tab-separated records. That format
existed to share a config directory with the bash original; this project shares
no code or config with either predecessor, so the compatibility constraint is
gone and AOT-safe serialization is worth more.

Recording rather than detecting is deliberate. `fleet-win` tried to find the
dashboard by matching pane titles and failed, because WezTerm reports the
foreground process and the dashboard is launched by typing into a shell — so the
title stays the shell's and the search never matches.

## Process model (the `embedded` driver only)

The `tmux` and `wezterm` drivers delegate process supervision and persistence to
the multiplexer itself and run no daemon. Everything in this section applies to
`embedded` alone.

```
  fleet (client)                 fleetd (daemon)                   children
  -------------                  ---------------                   --------
  Terminal.Gui dash --stream-->  session registry         --pty-->  nvim
  attach client     <-frames---  pane table                         claude
                    --input--->  per pane: PTY + libghostty-vt      shell
  fleet CLI verbs   --stream-->    terminal + frame renderer
```

The arrows are one protocol over any byte stream: a local pipe or socket, or SSH
stdio for a remote machine (*Remote attach*, below).

### fleetd

One daemon per user per machine. It owns every PTY and outlives all clients —
that single property is what buys detach and reattach.

Spawning it detached is platform-specific and easy to get subtly wrong: on
Windows, `CreateProcess` with `DETACHED_PROCESS` and no inherited handles; on
Unix, `setsid` with stdio redirected away from the parent's terminal. Double-fork
daemonization is not safe in .NET. On Windows it must also survive logout of the
OpenSSH session that started it (herdr hit exactly this).

### Transport

`\\.\pipe\fleet` on Windows, `$XDG_RUNTIME_DIR/fleet/<name>.sock` on Linux, and
`ssh -T <host> fleet bridge` for a remote daemon. Length-prefixed messages in both
directions, versioned by a handshake. The rules that keep SSH working are in
*Remote attach*. Both local sides are BCL types; this is the cheapest part of the
driver.

### Panes

Everything is a pane, Neovim included. This is the driver's private
representation; it satisfies the interface's `Pane` but carries more:

```
Pane { Id, Kind: Editor|Agent|Shell, Cwd, Argv, Pty, Terminal, Status }
```

`Terminal` is the pane's libghostty-vt instance. It is fed every byte the child
writes, whether or not anyone is attached, and it answers the child's terminal
queries (DSR, DA, mode reports) itself, so a pane with no client does not stall
on a query.

Consequence worth wanting: the editor session survives a dropped SSH connection
the same way the agents do.

### Attach

Fullscreen, one pane at a time. The client puts its console in raw mode, sends
input and resizes, and writes the frames it receives to stdout. fleetd renders
each frame by diffing the pane's emulator grid against what that client already
shows, as `spikes/EmbeddedSpike/Render/GridRenderer.cs` does, and sends only the
changed cells. Keys are encoded for the pane on the daemon side (see *Remote
attach*, rule 3).

### Repaint on attach — resolved 2026-09-24

The earlier plan replayed a per-pane ring buffer on attach and then poked a resize
so full-screen programs would redraw — a heuristic, not a screen model. It named
the upgrade: a headless emulator per pane that dumps exact screen state, as tmux
does. That is now the design. Attach, reattach, resize and reconnect after a
dropped link all send one full frame rendered from the emulator, and incremental
frames after it.

## Agent status and data flow

No screen scraping. Claude Code hooks report status to fleet directly, which
works the same whatever the mux is. `CaptureTextAsync` stays in the interface
regardless, as the fallback for harnesses with no hook system.

### Two daemons, not one

`fleet-win` uses one word for two different requirements, and separating them
matters:

- A **state daemon** holds agent state between hook invocations, because hooks
  fire in short-lived processes. `fleet-win` needs this even with WezTerm as the
  mux.
- A **PTY daemon** owns processes and outlives clients. Only `embedded` needs
  this.

Collapsing them would make `wezterm` and `tmux` inherit a background process they
have no use for.

### The hook contract — fixed

Independent of where state is stored.

| State | Reported by | Severity |
|---|---|---|
| `Blocked` | PermissionRequest, Notification | 3 — worth interrupting a human for |
| `Stalled` | **never reported** — derived | 2 |
| `Working` | UserPromptSubmit, PreToolUse | 1 |
| `Idle` | Stop, SessionStart | 0 |
| `Unknown` | no report exists | −1 |

Three pure rules on top, unit-testable with no terminal and no filesystem:

- **Aggregate** — several sessions sharing one pane collapse to the highest
  severity. One session on a permission prompt is the thing you need to know,
  even if another is mid-flight.
- **Derive** — `Working` past the stall threshold (default 600 s) presents as
  `Stalled`. Kept separate from storage so the raw reported state is what
  persists and presentation is computed.
- **MoreUrgent** — severity descending, then age descending. This is the
  dashboard sort order.

Invariant, carried from `herdr` through `fleet-win`: **subagent hook events must
never mark the parent pane done.**

A report also carries the harness's own transcript path, which is what makes cost
computable without asking the agent anything.

Correction against `fleet-win`: reports key on the **worktree directory**, not
the pane id. The hook runs inside the agent process with its cwd at the worktree,
so that is always available; a pane id may not be. This also matches agent
identity in the section above.

### Storage — daemonless files (decided 2026-10-02)

Decided for M2: **daemonless files**, behind the seam below. See "Live agent status
through hooks, 2026-10-02" for what was built. The options as they were weighed:

- **Daemonless files.** `fleet hook` writes one small file per agent,
  atomically; readers aggregate on read. No background process for `wezterm` or
  `tmux` at all, and fail-silent by construction — a missing file is an unknown
  agent, not an error path. The dashboard polls. `state.json` for the WezTerm Lua
  module needs a designated writer.
- **One `fleetd`, PTY half conditional.** Push notifications and a single writer
  for `state.json`, at the cost of a background process and its lifecycle on
  every driver.

Deferred until the `wezterm` driver is working and real usage can settle it. Kept
reversible by a seam, so the hook contract above is not blocked on the decision:

```csharp
public interface IAgentStateStore
{
    Task ReportAsync(AgentReport report, CancellationToken ct = default);
    Task<Snapshot> GetSnapshotAsync(CancellationToken ct = default);
}
```

`fleet hook` calls only `ReportAsync`. The dashboard calls only
`GetSnapshotAsync`. `FileAgentStateStore` and `DaemonAgentStateStore` are then a
same-day swap.

## Error handling

The predecessor's first invariant was **fail silent**: every call out to the mux,
git, nvim or the agent CLI degrades rather than errors. If the terminal is
closed, the daemon down, or nvim missing, each command falls back to a working
subset. In bash that was `2>/dev/null` and `|| true`; in Go it was deliberate
error swallowing at the call site.

C# inverts the default. Exceptions propagate unless stopped, which is exactly
backwards for this invariant, and a statically-typed rewrite of permissive shell
code is precisely where it gets lost.

**Enforce it once, with a decorator.** `FailSilentDriver` wraps any `IMuxDriver`,
catches expected failures, returns empty results, and records what it swallowed.
Command code is only ever handed the wrapped instance. This is the C# equivalent
of `fleet-win`'s single-package chokepoint, and it means the invariant is one
class to review rather than a habit to maintain at every call site.

Three carve-outs, because "swallow everything" is its own bug:

**Programmer errors still throw.** A malformed argument or a null where one is
impossible is not a degraded terminal. Catch the expected failure types, not
`Exception`.

**Destructive operations are loud.** `git worktree remove` failing must surface.
A teardown that silently "succeeds" while the worktree is still on disk — and its
done-marker already destroyed — is how state diverges from reality. Fail-silent
covers *reads* and *presentation*, never *destruction*.

**Silent is not invisible.** Everything swallowed goes to a rotating log, and
`fleet doctor` reports it. Otherwise the invariant turns a broken install into a
mystery.

## Testing

The predecessor had no tests; that is the one quality win available for free
here, and it is available because most of fleet is not about terminals at all.

**Pure, no terminal or filesystem needed** — harness parsing, branch slugging,
repo-layout detection, repo-name resolution including the ambiguity error, state
aggregation, stall derivation, urgency sort, cost summing, home contraction and
expansion. Test these as they are written.

**Against real git fixtures** — base-ref selection above all. Build a temp repo,
create genuine local/origin divergence, and assert that a local branch ahead of
origin is preferred. This is the trap that silently reverts unpushed merges, and
it cannot be caught by a unit test over mocks.

**Against the `fake` driver** — all command behaviour. Spawn, send, kill, jump,
teardown, restore. No terminal on either OS, so it runs anywhere and it is why
`fake` is first in the build order rather than an afterthought.

**Genuinely integration, on a CI matrix** — the PTY layer and the attach loop.
ConPTY and `openpty` cannot be faked into telling the truth, and neither can raw
mode. `windows-latest` and `ubuntu-latest`.

**AOT smoke test** — publish NativeAOT on both runners and run `fleet --help` and
`fleet doctor`. This is exactly what Terminal.Gui does upstream to keep AOT from
regressing, and it is the only thing that will catch a trimming break introduced
by a dependency bump.

`fleet doctor` remains the end-to-end smoke test, and must pass before any phase
counts as done.

## Layout — vertical slices

One folder per behaviour, holding everything needed to read or change it. Chosen
over layer projects because this codebase will be read and edited largely by an
LLM, and the two properties that matters most for that are **context locality**
— a change means reading one folder — and a **visible call graph**, where every
call is traceable by reading source rather than by guessing what a framework
does at runtime.

```
fleet.sln
src/Fleet/                            one project, AOT-published as `fleet`
  Program.cs                          composition root — the ONLY file naming a Platform type
  Shared/                             pure, no I/O: Result, ProjectName, HomePath
  Ports/                              interfaces and the types crossing them
    IFleetLog, Git/IGitRunner, Projects/IProjectStore, Mux/IMuxDriver
  Platform/                           every implementation that touches the outside world
    Logging/, Storage/, Git/, Mux/{DriverSelector, FailSilentDriver, Fake/, WezTerm/}
  Features/
    Projects/{PickProject, CreateProject, OpenProject}/
    Repositories/{BranchSlug.cs, AddRepository, ListRepositories}/
    Dashboard/ShowDashboard/
    Diagnostics/RunDoctor/
tests/Fleet.Tests/                    mirrors the slice tree
  Architecture/SliceBoundaryTests.cs
```

Later phases add slices rather than projects: `Features/Agents/{NewAgent, SendToAgent,
ReapAgent}/`, and `Platform/Mux/Tmux/`, `Platform/Mux/Embedded/`, `Platform/Pty/`.

### Rules, enforced by test not by discipline

1. A slice never references another slice. Where two slices must cooperate, the
   composition root passes a callback — `PickProject` takes a `Func<Project?>`
   rather than knowing `CreateProject` exists.
2. `Features/` never references `Platform/`. Only `Ports/` and `Shared/`.
3. Only `Program.cs` may name a `Platform` type.
4. `Ports/` depends on nothing but `Shared/`.
5. Sharing *within* an area is allowed and lives in the area root. Sharing
   *across* areas goes to `Shared/` and must be pure.

`SliceBoundaryTests` checks all four by scanning source text — chosen over
reflection because it catches references inside method bodies, which signature
reflection misses, and needs no IL parsing. Known gap: a violation written
without naming the namespace slips past.

### Supporting patterns

`Result<T>` for expected failures, so failure is in the signature rather than
hidden at a throw site; exceptions reserved for defects. Sealed records and no
inheritance, for a flat reading graph. Constructor injection only — no service
locator, no static mutable state, so any file is comprehensible alone. Four
ports total for all I/O, each with a fake.

### Rejected

**MediatR or any reflection-based dispatch** — two independent reasons. It breaks
NativeAOT (`IL2026`/`IL3050`, which `IsAotCompatible` promotes to build errors),
and it hides the call graph behind runtime resolution. Handlers are called
directly, by name.

**Repository pattern over git** — git *is* the store; a wrapper adding no
behaviour is a layer to read past. **DDD aggregates and domain events** — no
invariant here needs a consistency boundary. **Layer projects** (Core /
Application / Infrastructure / UI) — directly opposed to slicing; one behaviour
would touch four projects. **AutoMapper and convention magic** — reflection,
AOT-hostile, invisible behaviour.

### The tradeoff, stated

Project references enforce dependency direction at compile time; namespaces do
not. Collapsing to one source project trades that compile-time guarantee for a
test-time one. Worth it because six projects for a phase-1 TUI is ceremony, and a
violation caught by `dotnet test` is caught before it lands. If the architecture
tests prove insufficient, splitting `Platform` into its own project restores
compile-time enforcement mechanically.

## MVP

Eight commands. `fleet-win` reached 48; this is the subset that makes the first
build worth using rather than a step toward one.

> **Phase 1 is specified separately.** `docs/phase1.md` is the spec and
> `docs/PHASE1-PLAN.md` the implementation plan. Phase 1 narrows this MVP — a
> project picker instead of `fleet up`, bare repositories only, no agents yet —
> and the plan records every delta against this document.

| Command | Does |
|---|---|
| `doctor` | Verify the environment and report which driver was selected and why. The end-to-end smoke test |
| `up <project>` | Boot a project: resolve the root, create or adopt a session |
| `new <repo> <branch>` | Plan and create the worktree, spawn a window with an nvim pane and a claude pane |
| `ls` | List agents with state and age |
| `dash` | The Terminal.Gui dashboard: every agent sorted by urgency, jump on Enter |
| `send <agent> <text>` | Message a running agent |
| `reap <agent>` | Tear down: kill the window, remove the worktree, forget the record |
| `restore` | Rebuild the session from saved records after the terminal closed |

### Milestones

| # | Lands | Why here |
|---|---|---|
| M0 | Solution skeleton, `fake` driver, pure logic under test, CI matrix with the AOT smoke test | Nothing else is verifiable until the harness exists |
| M1 | `wezterm` driver, `doctor`, `up`, `new` | First real output: an agent on its own worktree with nvim and claude side by side |
| M2 | Hook reporter, `FileAgentStateStore`, `ls` | Makes state real, and settles the deferred storage question with evidence |
| M3 | `dash` | The product shape |
| M4 | `send`, `reap`, `restore` | Full lifecycle. Ship point |
| M5 | `tmux` driver | Proves the abstraction against a second real backend. SSH and headless Linux start working |

### Explicitly not in v1

`orchestrator` and `dispatch`, cost tracking, the write guard, keybinding
management, `browser`, `fan`, `watch`, the `embedded` driver, and tiling. Each is
a deliberate deferral, not an oversight — the design leaves room for all of them,
and none is needed to find out whether the core loop is good.

## The orchestrator — MCP, permissions, dispatch

The pane fleet opens on the left drives fleet through an MCP server
(`fleet mcp`), so an AI harness and a human reach the *same* handlers. Four
decisions shaped it.

**One tool identity, not two.** The MCP tool set and the per-project permission
set are the same enum (`HarnessTool`) in `Shared/Settings`. An MCP tool, its
permission rule, its settings-screen row, and its Claude rule-string
(`mcp__fleet__<id>`) all derive from one value, so the editor and the server
cannot drift. The `ServeMcp` slice adds descriptions and JSON-schema shape; it
does not re-declare the tools.

**JSON is AOT-safe by asymmetry.** NativeAOT forbids reflection-based
serialization. Inbound JSON-RPC is parsed *untyped* with `JsonDocument`: the
`id` is captured as raw text so a string, number, or null round-trips
byte-for-byte, and `arguments` are flattened to `Dictionary<string,string>` so
no `JsonElement` ever crosses into Ports or Features. Outbound frames are built
from source-generated DTOs, with the JSON-RPC envelope composed by string
concatenation around one serialized payload — no closed-generic registration.
`.mcp.json` and `.claude/settings.local.json` merges preserve unknown keys via
`[JsonExtensionData]`, so fleet never clobbers a user's own settings, and
refuses to write when the existing file is unparseable. A `Console.`-free
architecture test guards the MCP path, because a stray write to stdout is
indistinguishable from a protocol frame and kills the server silently.

**The gate, and why "ask" can mean "allow".** Every call passes one choke point
(`McpDispatcher`, serialized by a semaphore) that reads the project's policy:
allow runs it, forbid refuses it before any handler sees it, ask raises a
prompt. The subtlety: when the ask should prompt in fleet's own dialog, the
*Claude-side* rule is written as **allow** — otherwise Claude's permission
prompt fires first and fleet's gate never runs. Only an ask routed explicitly
to Claude's prompt is left for Claude to handle. So the planner maps
`Ask + FleetDialog → allow`, `Ask + ClaudePermission → ask`, `Forbid → deny`.

**Approvals ride the filesystem.** `fleet mcp` and `fleet dash` are separate
processes, so a dashboard prompt cannot be an in-process call. The MCP side
drops an `<id>.ask` and polls for an `<id>.reply`; the dashboard's existing
80 ms pump takes the oldest ask, shows an Allow/Deny dialog, and writes the
reply. A heartbeat file — touched every four seconds and while a dialog is open
— lets the MCP side fail fast with "no dashboard is running" instead of hanging
for two minutes when nobody can answer. Once an ask is picked up, a `.taken`
marker exempts it from the heartbeat check, so a slow human decision is never
mistaken for a dead dashboard.

## Open — not yet designed

- ~~**Agent state storage.**~~ Closed 2026-10-02: daemonless files behind
  `IAgentStateStore`. See "Live agent status through hooks, 2026-10-02".

## Verification log

**2026-08-08 — Terminal.Gui v2 under NativeAOT: cleared.**

The repository is `tui-cs/Terminal.Gui` (moved from `gui-cs`): 11.1k stars, last
push 2026-08-01, 51 open issues, not archived. Stable `2.4.17` on NuGet, with
`2.4.18-develop.*` prereleases — v2 is shipped, not a preview.

AOT is enforced in CI rather than merely claimed: #5251 added an AOT smoke test,
#5255 extended it to Windows and explicit clone paths, #4102 added AOT test
variants, #5402 restored AOT validation after examples moved out. Most
convincing, #5561 reported an AOT/trim regression (`IL2026`/`IL3050` from an
unannotated `TypeDescriptor.GetConverter`) and #5562 fixed it the same day. AOT
breakage is treated as a bug.

Also found: `Terminal.Gui.Interop.Spectre` (#5391–#5393) provides `SpectreView`,
rendering any Spectre.Console `IRenderable` inside Terminal.Gui — the two stack
choices compose officially.

Watch: #5607, "Conhost crashes on Windows 10", open. Windows console path.

**2026-08-08 — `wezterm-mux-server` headless attach: does not exist.** Checked
against the installed `wezterm 20260117-154428-05343b38`.

Control, headless: **works.** `wezterm cli --prefer-mux` is documented as
*"Prefer connecting to a background mux server. The default is to prefer
connecting to a running wezterm gui instance"*, and `--no-auto-start` implies it
will otherwise start one. `wezterm-mux-server` ships in the install and takes
`--daemonize`.

Attach, headless: **does not exist.** `wezterm connect` is a GUI client and only
a GUI client — every option is a windowing-system option: `--class` (*"Under X11
and Windows this changes the window class. Under Wayland this changes the
app_id"*), `--position` (screen coordinates, named monitors), `--new-tab`
(*"When spawning into an existing GUI instance"*). No text-mode flag exists.
`wezterm-mux-server --help` exposes only `--daemonize` and config options — no
frontend. `wezterm cli proxy` is *"start rpc proxy pipe"* and takes no arguments:
transport plumbing for SSH domains, not a human client.

So on a headless box fleet could drive panes and never see them. **`embedded`
remains required for headless Windows**, and the `Porta.Pty` AOT gate stands.

Salvaged: the `wezterm` driver's control path does not require a GUI. Worth
using — agents spawned from a hook or cron with no GUI up still work.

Risk noted: `wezterm cli` self-describes as *"Interact with experimental mux
server"*.

**2026-08-08 — `Pty.Net`: does not exist on NuGet.** The earlier plan named it;
that was wrong. It survives only as the `Quick.PtyNet` fork (6 stars, no declared
license), which is too thin to depend on. Replaced by `Porta.Pty`, which still
needs an AOT spike — see the PTY section.

**2026-08-08 — Terminal.Gui v2.4.17 under NativeAOT on Windows: cleared, by
building it.** `dotnet publish -c Release` with `IsAotCompatible`,
`EnableTrimAnalyzer` and `EnableAotAnalyzer` on and `TreatWarningsAsErrors`
produced zero `IL2026`/`IL3050` warnings, and the resulting `fleet.exe` runs
`--help` and `doctor` correctly. Binary size 20.25 MB with Terminal.Gui in use,
against a 1.0 MB baseline without it.

**2026-08-08 — Terminal.Gui v2.4 API differs substantially from what was
assumed.** Four corrections, all found by compiling rather than by reading:

- Namespaces are split: `Terminal.Gui.App` (Application), `.Views` (Window,
  Dialog, ListView, TextField, Button, Label, FrameView, CheckBox, MessageBox),
  `.ViewBase` (View, Dim, Pos), `.Input` (Key), `.Drawing` (LineStyle, Scheme).
  A bare `using Terminal.Gui;` compiles nothing.
- **The static `Application` facade is `[Obsolete]`** — "The legacy static
  Application object is going away." The instance model is
  `IApplication app = Application.Create().Init()`, disposed to shut down.
  `ApplicationImpl` is internal, so `Application.Create()` is the only entry.
  `MessageBox.ErrorQuery` now takes the `IApplication` as its first argument.
- **There is no `RadioGroup`**, and no `Colors`/`ColorScheme` — the latter
  replaced by `Scheme` / `SchemeManager` and `View.SchemeName`.
- `CommandEventArgs` carries only `Context`, with **no `Cancel`**. A `Dialog`'s
  own button handling therefore cannot be suppressed when validation fails, so
  fleet's modals are plain `Window`s, which close only when told to.
- `ListView.SelectedItem` is `int?`; activation is `Activated`, not
  `OpenSelectedItem`.

**2026-08-08 — the WezTerm JSON contract is confirmed against real output.**
`wezterm cli list --format json` on 20260117-154428-05343b38 emits exactly the
field names `WezTermPaneJson` declares. `WezTermDriver.ParsePanes` is public and
tested against captured output, so a renamed field fails a test rather than
surfacing at runtime. Note `cwd` arrives with a trailing slash
(`file:///C:/repos/fleet/`).

## Non-obvious behaviour

### `wezterm cli` needs to be told which mux — 2026-08-10

Running `fleet` from a terminal that is **not** a wezterm pane failed with "the
wezterm multiplexer did not respond", while the same binary worked from inside
wezterm. Cause: inside a pane, `WEZTERM_UNIX_SOCKET` names the mux to talk to.
Outside one, `wezterm cli` picks a socket itself, and with **two wezterm GUIs
running** it chose one it then failed to connect to:

```
failed to connect to Socket("gui-sock-186924")
```

Both sockets answered fine when the variable pointed at their full path, so the
sockets were live — only the auto-discovery was wrong. `FailSilentDriver` swallowed
the exception, `SpawnAsync` returned `PaneId.None`, and `OpenProjectHandler`
reported the mux as unreachable. Correct behaviour from a wrong premise, which is
why it read as a fleet bug.

`WezTermCli` now resolves the socket when the variable is absent: enumerate
`gui-sock-*` in wezterm's runtime directory, newest first, probe each with
`cli list`, keep the first that answers and reuse it for the process. An inherited
`WEZTERM_UNIX_SOCKET` is used as-is and never probed, so behaviour inside a pane is
unchanged.

**Consequence worth remembering:** with several wezterm GUIs running, fleet targets
the most recently used one that answers. Panes are numbered per mux, so a pane id
from one GUI means nothing to another — which is also why an earlier attempt looked
like it had opened nothing: the window existed, on the other GUI.



The codebase carries no comments by project convention, enforced by
`SliceBoundaryTests.No_source_file_contains_a_comment`. Everything that would
have been a comment lives here instead.

**Terminal.Gui v2.4 traps**

- `Enter` raises `Accepting` (`Command.Accept`). `Activated` is a *different*
  command and is not raised by Enter. Wiring `Activated` meant nothing could be
  opened from the picker.
- A parent `Window`'s `KeyDown` does not fire for keys the focused child claims
  through its own key bindings. Handlers belong on the focused view, or on
  `KeyDownNotHandled`. This is why `q` did nothing.
- The static `Application` facade is `[Obsolete]`. Use
  `IApplication app = Application.Create().Init()`; `ApplicationImpl` is
  internal. `MessageBox.Query`/`ErrorQuery` take the `IApplication` first and
  return `int?`.
- `CommandEventArgs` has no `Cancel`, so a `Dialog`'s button handling cannot be
  suppressed on failed validation. fleet's modals are plain `Window`s, which
  close only when told — that is what lets a rejected entry stay on screen.
- No `RadioGroup` exists; `CheckBox.Value` is a `CheckState`. No
  `Colors`/`ColorScheme`; use `Scheme`, `SchemeManager` and `View.SchemeName`.
- `ListView.SelectedItem` is `int?`. Motion commands are bound via
  `View.KeyBindings.Add(key, Command.Down)` and friends.
- `Terminal.Gui.Drawing.Attribute` collides with `System.Attribute` under
  `ImplicitUsings`; alias it.

**WezTerm driver**

- `activate-tab` takes `--tab-id`, not `--pane-id`.
- `cwd` arrives as a `file://` URL: `file:///C:/repos/x` on Windows, where the
  leading slash before the drive letter must be dropped, and `file:///home/red/x`
  on Unix, where it must be kept. It also has a trailing slash.
- WezTerm numbers panes from 0, so "no pane" cannot be 0. `PaneId.None` is empty.
- `--workspace` is rejected unless combined with `--new-window`.
- A WezTerm "tab" is what fleet calls a window.

**Git**

- `git worktree add` fails on a freshly `init --bare` repository because there is
  no HEAD commit. `AddRepositoryHandler` seeds one with plumbing: `mktree` on
  empty stdin, `commit-tree`, `update-ref`, `symbolic-ref`. Chosen over
  `worktree add --orphan`, which needs git 2.42+.
- Both git output streams must be drained concurrently with the wait, or a
  command producing more than the pipe buffer deadlocks.

**Platform**

- `Path.GetTempPath()` on Windows is inside the user profile, so it is not a
  valid "outside home" fixture.
- `Path.GetFullPath` does not validate characters on modern .NET; an illegal path
  is only refused by `Directory.CreateDirectory`.
- Records reserve the member name `Clone` (CS8859) — hence `CloneFrom`.
- `IsAotCompatible` must not be set repository-wide: xunit is reflection-based,
  and with `TreatWarningsAsErrors` it would fail the test build. It lives in
  `src/Fleet/Fleet.csproj` alone.
- NativeAOT on Windows needs `vswhere` on `PATH`; `install.ps1` adds it.
- Windows locks a running executable, so `install.ps1` stops running `fleet`
  processes before replacing the binary.
- `FailSilentDriver` catches only expected failure types, and `IsAvailableAsync`
  returning `false` is an answer rather than a swallowed failure.

**2026-08-08 — `Porta.Pty` under NativeAOT: FAILS. Gate resolved, answer is no.**

Proven by a spike in `spikes/PtySpike`, AOT-published and run.

With the analyzers on, ILC refuses outright: `IL2104`/`IL3053` from `Vanara.Core`
and `Vanara.PInvoke.Shared`, which use `TypeDescriptor.GetConverter` and
`BinaryFormatter.Serialize`. With warnings suppressed it publishes a 3.49 MB
binary, then **crashes at runtime before ConPTY is reached**:

```
System.ArgumentException: Unable to convert object to its binary format.
  at Vanara.Extensions.InteropExtensions.WriteNoChecks(...)
  at Vanara.PInvoke.Kernel32.SetInformationJobObject[T](...)
  at Porta.Pty.Windows.JobObject.Create()
  at Porta.Pty.Windows.PtyProvider.StartPseudoConsoleAsync(...)
```

Vanara marshals generic structs via `BinaryFormatter`, which NativeAOT strips.
Suppressing the warning hides the diagnosis, not the defect.

Confirmed working in the same spike: prefix detection over the raw input byte
stream, including `Ctrl+S` arriving as `0x13`. The keyboard-interception half of
the attach model is sound; only the PTY library is not.

**2026-08-08 — hand-written ConPTY under NativeAOT: WORKS. This is the viable path.**

Same spike, retargeted at our own `LibraryImport` P/Invoke with zero third-party
dependencies. Published with `IsAotCompatible`, both analyzers, and
warnings-as-errors:

- **0 IL warnings, 1.63 MB** binary — against `Porta.Pty`'s hard failure and
  3.49 MB.
- `CreatePseudoConsole` returns `HRESULT 0` with a valid `HPCON`.
- `ResizePseudoConsole` accepted.
- Pipe I/O works: ConPTY's own init stream (`ESC[?9001h ESC[?1004h`) arrives.
- Prefix scanning over raw input bytes works, `Ctrl+S` as `0x13` included.

So the AOT gate is cleared.

Child attachment could not be verified in this harness: every native call reports
success, but the child's output arrives on the parent's stdout instead of through
the pipe, of which only ConPTY's 16 handshake bytes are received.

Ruled out by experiment, so they need not be retried:

| Hypothesis | Result |
|---|---|
| `STARTUPINFOEX.cb` should be `sizeof(STARTUPINFO)` | No — 104 gives `ERROR_INVALID_PARAMETER`; 112 is correct |
| `lpValue` should be a pointer to the `HPCON` | No — captures 0 bytes, worse than by-value |
| `bInheritHandles` should be `true` | No change |
| Short-lived child exits before ConPTY renders | No — a chatty child behaves identically |
| A bug in our own P/Invoke | **No — see below** |

**2026-08-08 — RoyalApps PTY under NativeAOT: also works, and it exonerates our
ConPTY code.**

`RoyalApps.RoyalTerminal.Terminal.Pty.Platform` 0.5.0 publishes with strict
analyzers, **0 IL warnings, 1.91 MB**. Pure managed, no native shims, all
`net10.0`, no Vanara. API is callback-based: `DefaultPtyFactory().Create()` then
`Start(shell, columns, rows, workingDirectory, environment, arguments)`,
`DataReceived`, `ProcessExited`, `Resize`, `Write`, `Stop`.

It then produced **byte-for-byte the same failure** as our hand-written ConPTY:
same 16 handshake bytes, child output on the parent's stdout, resize accepted.
Two unrelated implementations failing identically pointed at the harness, and it
was: the automation shell used for these runs has **no console**.
`Console.WindowWidth` throws `IOException`, and both
`Console.IsOutputRedirected` and `Console.IsInputRedirected` are `true`.

ConPTY exists to host a console for a child. With the parent's stdio redirected
to pipes there is no terminal to hand off to, so the child never lands on the
pseudoconsole. **The earlier conclusion that this was a bug in our ~200 lines was
wrong.** Both implementations are probably correct; neither can be verified
except from a real terminal.

Outstanding, and only a human at a real terminal can answer it: run
`spikes/royalout/royalptyspike.exe --attach nvim` and `--attach claude` inside a
WezTerm pane, and confirm that the child renders correctly, that `ctrl+s space`
draws the overlay, and that the child repaints after dismissal.

Recommendation if that passes: prefer RoyalApps over hand-written ConPTY. It is
AOT-clean, cross-platform including Unix, and removes the P/Invoke and the
fork-safety problem from fleet's own codebase.

Alternatives, with dependency graphs checked:

- **Hand-written ConPTY.** `LibraryImport` source generators, zero dependencies,
  guaranteed AOT-safe. The API set is already listed under *Remaining P/Invoke*.
  Windows only; the Unix controlling-terminal problem remains unsolved.
- `RoyalApps.RoyalTerminal.Terminal.Pty.{Windows,Unix,Platform}` — target
  `net10.0` and do **not** depend on Vanara. Untested under AOT.
- `Microsoft.Windows.Console.ConPTY` — no dependencies at all, Microsoft, but
  Windows-only and preview.

**2026-08-08 — keyboard and code page facts, measured in a real WezTerm pane.**

With `ENABLE_VIRTUAL_TERMINAL_INPUT` set, single bytes arrive for control chords:

| Key | Byte |
|---|---|
| `ctrl+space` | `0x00` |
| `space` | `0x20` |
| `ctrl+a` | `0x01` |
| `ctrl+g` | `0x07` |
| `ctrl+q` | `0x11` |
| `ctrl+s` | **never arrives** — WezTerm claims it as a tmux-style leader |

`ctrl+s` is unusable as fleet's prefix on this machine: `~/.wezterm.lua` enables a
`CTRL+s` leader. The default prefix is therefore `Ctrl+Space`.

**A pane fleet owns must set the console code page to UTF-8.** Measured
`wasOutputCP=437`, so writing a child's UTF-8 output straight through renders
box-drawing characters as CP437 mojibake (`─` appears as `Гôç`). `SetConsoleOutputCP(65001)`
and `SetConsoleCP(65001)`, restored on exit, fix it.

**Process note, learned the hard way:** Windows locks a running executable, so
publishing over a spike that is still attached fails with `MSB3027` after ten
retries. Filtering publish output to `IL` warnings hides that error and yields a
stale binary that looks freshly built. Kill running instances first, and check
the timestamp.

**2026-08-08 — owning a pane requires a VT *input* parser, not a byte check.**

Traced from a real `--attach nvim` run. Once the child is running, stdin no longer
delivers single bytes:

```
ESC [ 13;28;13;0;0;1 _      win32-input-mode key event
ESC [ <0;33;14 M            SGR mouse report
```

nvim requests win32-input-mode and mouse tracking (ConPTY advertises the former
with `ESC[?9001h`), so the terminal re-encodes **all** input. `ctrl+space` is
`0x00` only while no child has switched modes; under nvim it becomes a `CSI … _`
sequence. A single-byte prefix scanner cannot work.

Consequences for the `embedded` driver, all newly known:

1. Prefix detection needs a real VT input parser — at minimum recognising
   `CSI … _` win32-input-mode events and `CSI … M/m` mouse reports, passing
   everything else through untouched.
2. The parser must be transparent: anything fleet does not claim has to reach the
   child byte-for-byte, or nvim's own keys break.
3. A cheaper interim option exists: fleet controls the child's output stream, so
   it can **filter the mode-setting requests it cannot yet handle** — strip
   `ESC[?9001h` and the mouse-enable sequences — and input stays legacy
   single-byte. The cost is that the child loses enhanced key reporting and mouse
   support while that filter is in place.

This is on top of the already-known repaint-on-dismiss heuristic, and it is why
the mux-intercepts-prefix option remains materially cheaper: tmux and WezTerm
already contain input parsers.

## Settled: how the fleet menu is reached

**The multiplexer intercepts the prefix; the menu is a WezTerm overlay.**

`fleet apply-keybinds` generates `~/.wezterm/fleet.lua` from the current keymap.
It binds the prefix chord to a WezTerm `InputSelector` — a centred overlay drawn
on top of the window, with fuzzy filtering — whose entries are fleet actions.
Choosing one opens `fleet menu --action <id>` in a split, where fleet's own
themed views do the work.

Why this and not fleet owning the pane:

- It works in **any** pane, including one running only claude, because WezTerm
  claims the key before the pane's program sees it. This is exactly how tmux's
  prefix works.
- It needs no PTY, no VT input parser, and no repaint heuristic — the three costs
  the spikes measured.
- tmux gets the same behaviour later via `bind-key`, so the approach generalises.

Accepted trade-off: the overlay is drawn and styled by WezTerm, not `FleetTheme`.
Fleet's styling resumes in the split that follows. A floating centred OS window
would keep every pixel under `FleetTheme` but takes focus and appears in the
taskbar.

A single chord, not a leader: WezTerm supports one `leader` and a user may
already have one (this machine uses `CTRL+s` for a tmux mode), so fleet inserts
into `config.keys` instead. Default prefix is `Ctrl+Space`.

### Where the chosen action runs — 2026-08-08

The first cut ran every menu choice as `fleet menu --action <id>` in a fresh
split. For **Add repository** that was wrong twice over: a third column appeared
beside claude and the dashboard, squeezing both, and the form was rendered by a
process with no idea a dashboard existed.

Split by who can draw the view:

- Actions the dashboard already draws — add repository, keybinds, refresh — are
  **handed to the running dashboard**. `fleet request --action <id> --project
  <name>` writes one file to `%APPDATA%\fleet\requests\<project>.request`;
  `ShowDashboardView` polls it every 200 ms through `IApplication.AddTimeout` and
  dispatches it exactly as if the key had been pressed. The form fills the pane
  it belongs to and nothing is resized.
- Everything else — opening another project, the picker — still gets a split,
  because there is no running view to hand it to.

`DashboardActions.Served` is the single list both sides read: it drives the
generated Lua's `M.dashboard_actions` table and documents the split in one place.

Two properties make the file store adequate without a daemon:

- **Latest wins, no queue.** One file per project, overwritten. A user who picks
  twice before the dashboard polls gets the second choice, which is what they
  meant.
- **Read-then-delete.** `TakePending` consumes the request, so a form cannot open
  twice. A torn read during the writer's `WriteAllText` throws `IOException`,
  is swallowed, and the next tick retries — the file is still there.

The address is the WezTerm user var. The dashboard now publishes its **project
name** as the value of `fleet` rather than the constant `"dashboard"`, so the
same var answers both "is fleet in this window?" and "which dashboard do I send
this to?". WezTerm's `wezterm.background_child_process` runs the request with no
pane at all, so nothing flashes on screen.

A `busy` flag guards the poll: Terminal.Gui timeouts keep firing inside nested
`Run` loops, so without it a second request would stack a modal on top of the
open one.

**Add repository has no bare key.** It is menu-only, and `AddRepository` was
removed from `KeymapDefaults.Bindings` and `Configurable` rather than merely left
unhandled — a key shown in the keybinds editor that does nothing is worse than no
key. Consequence: a saved `keybinds.json` from before this change still contains
`AddRepository`, and `Keymap`'s constructor indexed `KeymapDefaults.Bindings[action]`
directly, so it would have thrown `KeyNotFoundException` on load. `MergedOverDefaults`
now drops bindings for actions fleet no longer has, and the lookup is a
`TryGetValue`. Any future retired action is safe by the same route.

### The dashboard's two sections are tabs — 2026-08-09

Agents first and selected on open, Repositories second, switched with `h` / `l`
or the arrow keys. Movement **clamps at the ends, it does not wrap**: with two
tabs, wrapping makes `h` and `l` do the identical thing from either position, so
`h` never behaves as "left" and reads as a dead key. Clamping is also what vim
does with `h` at column 0. Counts live in the titles (`Repositories (1)`) because a tab
hides the other section entirely — without them the dashboard can only answer
"how many agents?" by switching away from what you are reading.

**Rendered by `FleetTabBar`, not `Terminal.Gui.Views.Tabs`.** The widget was
tried first and rejected on looks: it draws a full bordered box for the tab strip
*inside* the window's own border, which is the panel-in-panel the UI is supposed
to avoid. `FleetTabBar` is a plain row of `Label`s plus a rule under the selected
one — the active label takes the section scheme, the others the hint scheme, and
the content lists are swapped with `Visible`. Every cell is under `FleetTheme`.

The widget also had a repaint trap worth recording in case it comes back: setting
a tab's `Title` does not repaint its header. The header span and offsets are
recomputed during *layout*, so `SetNeedsDraw()` on the container or on the tab's
`Border` leaves the old text until some unrelated event forces a layout. Pressing
a key fixed the display, which is what gave the diagnosis. `FleetTabBar` sidesteps
this by owning its own arrangement.

**Keys must be bound at the window, not the list.** The handler was originally on
each `ListView`'s `KeyDown`. As soon as the lists sat inside tab containers focus
could land on a container instead, so no list raised `KeyDown`: Escape fell
through to `Window`'s default and closed the pane, and `h`/`l` reached nothing.
`window.KeyDownNotHandled` fires wherever focus sits while still letting the
focused list consume its own motions first.

This regression is why a live check that "passed" is not proof: the first
`wezterm cli` run happened to leave focus on the list, so it saw none of it. A
UI check has to name the focus it is testing under, or it is testing luck.

**Keys are now resolved against a scope.** `h`/`l` for tabs put `l` on both
`NextTab` and `OpenProject`, repeating the `k` = `MoveUp`/`EditKeybinds`
collision from the day before. Rather than avoid shared keys, `Keymap.ActionFor`
gained an overload taking the caller's list of candidate actions and resolving in
that order. The dashboard asks for `Close, Refresh, PrevTab, NextTab`; the picker
asks for its own. A shared key is now deterministic by construction instead of
depending on dictionary enumeration order, and vim-style keys can mean different
things in different views — which is the point of them.

### The composition root is a folder, not a file — 2026-08-09

`Program.cs` had grown to 478 lines doing four jobs: parsing args, constructing
adapters, running each command, and formatting output. `Dash` alone was 115 lines,
most of it the `DashboardCallbacks` block.

```
Cli/
  CommandLine.cs          args -> Invocation          pure, now tested
  Runner.cs               verb -> command
  Commands/               one file per verb
  Composition/            the only place that names Fleet.Platform
Program.cs                8 lines
```

Two things this bought beyond tidiness. Argument parsing had **no tests at all**
and now has nine, including the case where a flag is last with no value after it.
And the rule that was "only `Program.cs` may reference `Platform`" — true only
because one file happened to be the root — is now "only `Cli/Composition` may",
which is what was actually meant. A second test keeps `Cli/Commands` itself free
of `Platform`, so commands wire features together without reaching for adapters.

`Adapters` returns a `MuxSelection` record rather than the old `out string chosen,
out string? unsupported`, and the mux is still constructed per command rather than
eagerly — `fleet request` runs on every menu pick and must not pay for a PATH
probe it never uses.

A test asserts `Program.cs` stays under 20 lines. That is the whole point of the
refactor, so it is worth failing a build over.

### Agents, first slice — 2026-08-09

Spawn and list. `n` on the dashboard opens a form for the repository selected in
the Repositories tab, plans a worktree, cuts the branch, spawns the harness in
it, records it, and lists it under Agents. `enter` focuses an agent's pane,
matched **by worktree path** — never a pane id, per the identity rule above.

Storage: **daemonless files**, the option DESIGN.md deferred until the wezterm
driver worked. Agent records live in `sessions/<project>.json`; the dashboard
reads on refresh. No background process on any driver.

### The nvim harness opens neo-tree and claude — 2026-08-09

`AgentHarness.CommandFor` turns a harness into an argv. `claude` is just
`["claude"]`; `nvim` is
`["nvim", "-c", "lua vim.schedule(function() vim.cmd('Neotree show') vim.cmd('ClaudeCode') end)"]`.

`vim.schedule` rather than two bare `-c` commands: both plugins are lazy-loaded,
and deferring to the event loop lets lazy.nvim resolve the `:Neotree` and
`:ClaudeCode` command triggers before they are called. The command names come
from the user's own config (`neo-tree.lua`, `claudecode.lua`) — fleet is coupled
to that config, which is worth remembering if the plugin set changes.

The whole lua string is a single argv element and reaches wezterm through
`ProcessStartInfo.ArgumentList`, so .NET does the quoting and no shell is involved.

### The menu is fleet's, not WezTerm's — 2026-08-10

The prefix now runs `fleet menu --project <name>` in a **new tab**; fleet draws the
menu itself with `FleetTheme`, and every entry shows its key. A tab rather than a
split so no pane is resized, and it closes itself when the action finishes.

This removed more than it added. Gone from the generated Lua: the `InputSelector`,
the key table that replaced it, `M.menu`, `M.dashboard_actions`, `M.run` and the
routing between "hand it to the dashboard" and "open a pane". The Lua is now two
things — gate on a fleet pane being present, and spawn the menu. Everything else is
C#, which is also testable.

Two earlier attempts are worth recording as dead ends: `InputSelector` with
`fuzzy = true` (no per-entry keys), then `alphabet` (keys, but WezTerm owns the
rendering and the styling), then a one-shot key table (keys, but WezTerm draws no
menu at all). Owning the menu was the answer the whole way along; the reason not to
was that the prefix must work from a claude pane, and spawning a tab solves that
without fleet owning anyone's PTY.

**Every fleet picker now carries keys.** `PickerKeys` assigns the first free letter
of each label and `FleetPicker` renders `key  label` and selects on that key, so the
manage menu and the harness picker got keys for free.

### Opening a hidden agent unhides it — 2026-08-10

Opening a hidden agent used to spawn it in the hidden workspace, which put a new
WezTerm window in front of the user. `OpenAgentHandler` now takes the project root,
finds the project's window, and **moves the pane back into it** — clearing `Hidden`
in the record — before focusing. A hidden agent with no pane restarts visible in the
project window rather than hidden.

Because opening always brings an agent back, nothing needs to switch workspace any
more; the `update-status` bridge stays only for the case where a user is parked in
the hidden workspace themselves.

**A bug of my own making, found in fleet's log.** Moving a pane silently did nothing
while the record still flipped. `FailSilentDriver` swallowed the reason and
`fleet.log` had it exactly: `--window-id cannot be used multiple times`. A patch had
added a `--window-id` block by matching a snippet that appears in *both* `SpawnAsync`
and `MovePaneAsync`, so the move emitted it twice. Both argv builders are now
`static` and tested directly — the flags are a pure function of the options, and
that is the level at which this class of mistake is catchable.

### Repositories are openable, pullable and configurable — 2026-08-10

The Repositories tab stopped being a read-only list. `enter` opens a repository in
a pane — a plain shell, not an agent, because the point is pulling and reading
code rather than driving a harness. `p` pulls it. `m` manages it.

Where "the repository" is on disk matters: fleet's layout is a bare container with
worktrees under it, and neither `pull` nor a shell is useful in a bare repository.
`RepositoryWorktree.For` resolves the **default branch's checkout** and falls back
to the container only when that worktree is missing, so `enter` and `p` land
somewhere with files in it.

Pull is `fetch --prune` in the container followed by `merge --ff-only` in that
worktree — never a plain `pull`, so it can't create a merge commit in a checkout
an agent may be sharing. A fetch that succeeds while the fast-forward cannot is
reported as a success with a reason, because the fetch is the part that matters.

**Manage changes the default branch only.** `symbolic-ref HEAD` is bookkeeping: it
decides what future agents cut from and what `p` fast-forwards. It deliberately
does **not** switch any worktree's checkout — that would move the ground under an
agent working there, and would fail outright on a dirty worktree. The status line
says "its worktrees are untouched" so the narrower meaning is visible.

**The new-agent form pre-fills Base with the chosen repository's default branch**
rather than a placeholder, and follows the repository when that changes. Empty
still means "the default branch", but showing the name means the user can see what
they are cutting from without opening the picker.

### Rows read branch, repository, status — 2026-08-10

An agent row leads with its **branch**, then the repository, then a git status in
lazygit's shape: `` for behind/ahead, `*` when the worktree is dirty.
The harness column is gone — it was the least interesting thing on the row and is
one keypress away in the manage menu. Repositories carry the same glyph and status.

**Three fallbacks are needed to get a count at all**, discovered by watching the
status stay stubbornly empty:

1. `@{upstream}` — the obvious form, and **never** configured here: a worktree made
   by `git worktree add` from a bare clone has no upstream, so this always failed.
2. `origin/<branch>` — works for repository branches like `develop`.
3. The agent's recorded **base ref** — needed because an agent branch such as `dev`
   is local-only, so `origin/dev` does not exist either.

An empty status therefore means "level with whatever it can be compared to", not
"unknown", and `BranchState.Unknown` is reserved for a worktree that is gone.

`BranchStatus` lives in `Ui` and takes a `BranchState` from `Shared` — the type
started in `Ports/Git/Models`, which the architecture test rejected immediately,
since `Ui` may depend on nothing but `Shared`. It is a domain value, not an I/O
contract, so `Shared` was where it belonged.

### The bottom bar is buttons — 2026-08-10

`FleetActionBar` replaces the hint label with a row of `Button`s, so every shortcut
is also clickable. They are built with `CanFocus = false` and handled on
`MouseEvent`/`LeftButtonClicked`: focusable buttons would join the Tab order and
steal focus from the list, which is the thing the user actually navigates. Keys and
clicks run the same delegates, so there is one definition of what each entry does.

The bar is rebuilt per tab, which is also what keeps the two key scopes and the two
visible sets in step.

### Opening a repository opens nvim — 2026-08-10

`enter` on a repository was a bare shell. It now runs nvim with the tree open
(`AgentHarness.BrowseCommand`) — but deliberately **not** `ClaudeCode`, unlike the
nvim harness for agents: a repository pane is for pulling and reading, and starting
a second claude there would be a surprise.

`nvim` is also the default for new agents now, and `Describe` returns the plain
name, so the button reads `nvim` rather than explaining itself.

### Keys are scoped to the visible tab### Keys are scoped to the visible tab### Keys are scoped to the visible tab — 2026-08-09

`n` and `d` mean different things on each tab: new agent / add repository, manage
agent / remove repository. `DashboardKeys.For` takes the selected tab and resolves
against `AgentScope` or `RepositoryScope`, which is the same scoped-resolution
mechanism that lets `l` mean "next tab" here and "open" in the picker. Hiding and
the harness picker simply are not in the repository scope, so those keys do
nothing there rather than doing something meaningless.

The hint bar follows the tab and lists only actions, not motions — `j/k`, `g/G`
and `h/l` were noise once the tabs made the bar longer than the pane.

`m` on an agent opens a picker holding **everything that acts on one agent**:
change what it opens, hide or show it, stop it, remove it keeping its files, remove
it with its worktree. Per-agent settings and per-agent destruction belong in the
same place, which leaves the dashboard's top level as just *new*, *open*, *manage*.
`c`, `x` and the old `d` lost their bare keys.

`d` on a repository deletes the repository and every worktree under it. Two
guards: it refuses outright while any agent is registered on that repository, and
the confirmation lists every worktree that would go plus any branch that is not
pushed. This is the most destructive action fleet has.

**Trap fixed, not just re-encountered.** A saved `keybinds.json` held the *whole*
keymap, so it pinned every shipped default at the moment it was written and later
changes never reached the user — `AddRepository` stayed on `a`, then `RemoveAgent`
stayed on `d` after it moved to `m`. `JsonKeymapStore.Save` now writes only bindings
that **differ** from `KeymapDefaults` (`KeymapDiff.AgainstDefaults`) and drops
actions fleet no longer has. A user who never rebinds anything ends up with an empty
`bindings` object and follows the defaults forever.

### Stopping and removing agents — 2026-08-09

Two separate actions, because they destroy different amounts:

- **Stop** (`s`) kills the agent's pane and leaves the worktree and the record
  alone. `enter` starts it again.
- **Remove** (`d`) stops it, removes the worktree, and drops the record. The
  **branch is kept** — removing an agent is not deleting work.

All four teardown traps from the section above are now paid for, with tests:

1. **Dirty check ignores `.fleet/`.** Counting fleet's own untracked files would
   make every fleet-spawned worktree permanently dirty. Verified live: a worktree
   holding both `wip-notes.txt` and `.fleet/ready` reported exactly one change.
2. **`.git` is verified at the directory first.** `git status` walks *up* until it
   finds a repository, so a half-removed worktree would otherwise report whatever
   encloses it — and a teardown decision made on another repo's cleanliness
   deletes the wrong thing. `IsWorktree` checks for `.git` as either a directory
   or a file, since a linked worktree's `.git` is a file.
3. **Order: inspect → `worktree remove --force` → `worktree prune`.** The force
   flag is needed because git's own check does not ignore `.fleet/`. Nothing
   deletes `.fleet/` first: a removal that then failed would leave the worktree in
   place with its markers destroyed.
4. **Removal runs from the main worktree**, resolved via `rev-parse
   --git-common-dir` and taking that directory's parent — the worktree's own
   parent is the container, which is not a repository at all.

The dirty state is shown in the confirmation rather than used to refuse. Refusing
would strand a user whose only uncommitted file is one they do not want; naming
the files and the count keeps the decision theirs while making it deliberate.

`RemoveAgent` is dispatched through the deferred path with the rest of the
view-opening actions, and the git and mux calls it makes are blocking rather than
awaited — a modal cannot be opened after an `await` (see the
SynchronizationContext note), and the whole flow is modal anyway.

### What an agent opens, and hiding it — 2026-08-09

**Harness is per agent and changeable.** `claude` or `nvim` (claude launched from
inside it by the user's own config — fleet starts nvim and stays out of the way).
Chosen on the new-agent form, and `c` on the dashboard re-picks it for an existing
agent. The change is recorded and takes effect the next time the agent starts;
nothing restarts a running process behind the user's back.

**Hidden means "in another workspace".** WezTerm has no API to hide a tab, so a
hidden agent is moved to the `fleet-hidden` workspace: gone from this window's tab
bar, still running, and still listed in fleet's Agents pane marked `(hidden)` —
hiding is a terminal concern, never a fleet-listing one.

Bringing one back needed a mechanism fleet did not have. **The CLI cannot switch
workspaces**: `wezterm cli activate-pane` on a pane in another workspace returns
exit 0 and leaves `list-clients` reporting the old workspace. Verified twice, once
before designing this and once after. So fleet writes the wanted workspace to
`requests/workspace.request` and the generated Lua performs the switch from an
`update-status` handler — the only periodic callback WezTerm offers, firing about
once a second. Same request-file shape as the menu actions.

**A path bug this uncovered, worth recording.** `CwdUrl.Normalize` returns forward
slashes (`C:/repos/...`) while an agent's worktree is recorded with backslashes, so
`PathKey.Same` never matched on Windows. Hiding silently did nothing, and "open
agent" had been *respawning* rather than focusing an existing pane — it looked
correct because a new pane appeared. No test caught it because `FakeMuxDriver`
stores whatever cwd it is given, so fake and real never disagreed. `PathKey` now
folds separators on Windows, and the test asserts a real wezterm cwd against a real
recorded worktree.

### Starting an agent: branch name and base — 2026-08-09

The form is three rows. **Repo** and **Base** are buttons that open a
`FleetPicker` overlay; **Branch name** is the only typed field. They were
read-only text fields first, which was wrong: a field that looks exactly like the
editable one beside it reads as somewhere to type, not somewhere to press. The
affordance has to match the behaviour.

`FleetTheme.Choice` sets `HotKeySpecifier` to a character that cannot occur, so a
button labelled `backend` does not claim `b` and swallow it from the branch-name
field beside it — the default would have made the first letter a hotkey that fires
regardless of focus.

What the pair means:

| Branch name | Base | Result |
|---|---|---|
| given | given | cut that branch from that base |
| given | empty | cut that branch from the default branch |
| empty | given | work on the base branch itself, no new branch |
| empty | empty | refused — there is nothing to work on |

`AgentBranch.Plan` is the pure function holding that table, so the rule is tested
without a terminal. A remote base with no branch name reduces to a local branch of
the same short name (`origin/develop` → work on `develop`), which is what
`worktree add -b develop origin/develop` does anyway.

**`clone --bare` leaves a repository with no remotes.** Discovered while building
the base picker: it copies the remote's branches straight into `refs/heads` and
writes **no fetch refspec**, so `refs/remotes` is empty and `git fetch` can never
learn about new upstream branches. The user's `backend` had 18 local refs and 0
remote ones. `AddRepository` now sets
`remote.origin.fetch = +refs/heads/*:refs/remotes/origin/*` and fetches once, so
remote-tracking branches exist. This also makes trap 1 meaningful — the local
versus `origin/` comparison had nothing to compare against before.

The picker hides a remote whose short name already exists locally, so the list
does not double up now that both ref namespaces are populated.

**Restore is the same action as focus.** `enter` looks for a pane whose cwd is the
agent's worktree; if there is none, it spawns the recorded harness there rather
than reporting a dead agent. This is the payoff of keying identity on the worktree
path: after the terminal closes, the record still describes everything needed to
bring the agent back, and no pane id had to survive. A worktree that no longer
exists on disk is reported instead — that is a deleted agent, not a stopped one.

**Nothing on the dashboard closes it.** It is the project's main pane, so `q` was
removed from its key scope alongside `esc`; `Close this pane` moved into the
WezTerm menu, which routes it back through `fleet request` like the other
dashboard-served actions. Closing is now always deliberate. The picker keeps `q`,
which is why key scoping had to exist first.

Ported from the traps section, with tests: base-ref selection prefers the local
branch when it is ahead of origin (so unpushed merges are not silently reverted),
and the default branch resolves `origin/HEAD` → the anchor's own `HEAD` → `main`
rather than jumping to a hardcoded `main`. Reap and the dirty check are **not**
built — teardown order is the part that destroys work if half-done.

### Terminal.Gui v2 has no SynchronizationContext — 2026-08-09

Three separate bugs in one afternoon came from this, so it is worth stating
plainly. `Terminal.Gui` never installs a `SynchronizationContext`, so
`ConfigureAwait(true)` is a **no-op**: every continuation after an `await` runs
on a thread-pool thread. Consequences, all observed:

- Opening a view after an `await` deadlocks the app — the modal's nested `Run`
  starts on the wrong thread and renders nothing while still taking keys.
- Any UI mutation after an `await` must go through `IApplication.Invoke`, which
  queues to the next main-loop iteration (or runs inline if already on it).

The dashboard's rule: **async work returns data; the UI is touched only inside
`app.Invoke`**, and a view is only ever opened before the first `await` of a
handler. `ReportingAsync` wraps fire-and-forget work so a failure lands in the
status line instead of vanishing into a discarded `Task` — which is what hid the
first instance of this for three build cycles.

### Dashboard keys are claimed at the application, not the view — 2026-08-09

A focused `ListView` swallows printable keys for its type-to-search
(`CollectionNavigator`), and it does so in `OnKeyDown`, before the `KeyDown`
event is raised — so neither a handler on the list nor `window.KeyDownNotHandled`
ever sees them. Only keys with an explicit `KeyBindings` entry survive, which is
why `j`/`k` worked and `n` did not, and why the earlier per-list handler appeared
to work for `a`/`r`/`q` before the lists gained focus.

The dashboard now subscribes to `app.Keyboard.KeyDown`, which fires before any
view dispatch, and unsubscribes in the `finally` beside `window.Dispose()`. It
ignores keys while `busy`, so a form's own fields keep their input rather than
having `q` or `n` stolen by the dashboard underneath.

Diagnosis note for next time: the decisive evidence was writing the received key
into the status line. Three theories (navigator matching, key-dispatch
re-entrancy, z-order) all survived reasoning and all died to one line of
instrumentation showing `j` arriving and `n` not.

**The `embedded` driver is parked, not cancelled.** Everything the spikes proved
still holds if headless Windows ever forces it: RoyalApps PTY and hand-written
ConPTY are both NativeAOT-clean, and the remaining work is the input parser and
the repaint heuristic. The spikes stay in `spikes/` as evidence.

**Trap worth remembering:** a saved `keybinds.json` overrides `KeymapDefaults`
entirely. Once a user rebinds anything, the editor persists the whole keymap, so
later changes to the shipped defaults never reach them. Changing a default is not
enough to fix an existing install.

## Coloured rows, 2026-08-10

A `ListView` row bound to `ObservableCollection<string>` is one string drawn with
one attribute, so a green `↑1` beside a yellow `↓4` is impossible through
`SetSource`. Rows are now `FleetRow` — an ordered list of `FleetSpan(Text, Tone)`
— rendered by `FleetRowSource`, a hand-written `IListDataSource`. Its `Render`
walks the spans rune by rune, honours the viewport's horizontal offset, sets the
attribute per span through `FleetInk.For(tone, basis)`, and pads the rest of the
row so the selection bar still spans the full width. `basis` comes from
`GetAttributeForRole(selected ? Focus : Normal)`, so the selected row keeps its
highlight and each span keeps its own foreground.

Two details the interface forces: `IListDataSource` extends `IDisposable`, and
`ToList()` is what the list's type-to-search reads, so it returns the flattened
`Text` of each row. `CollectionChanged` is implemented with empty accessors —
rows are replaced by assigning a new `Source`, and the pull spinner mutates one
row through `Replace(index, row)` followed by `SetNeedsDraw()`.

Tones live in `Ui/Constants/FleetTones.cs` as string constants (matching
`FleetSchemes`) rather than an enum, and map to palette colours in `FleetInk`.
The git status is now a lualine-style pill:  edge, git icon, then `↓n` yellow,
`↑n` green, dirty red, then the closing edge — all on `Surface0`, with the edge
glyphs drawn in `Surface0` against the row background so the pill reads as a
rounded shape. The branch name lost its icon, since the pill now carries it.

**A write stripped a private-use glyph.** `FleetGlyphs.Branch` was `""` — an
empty string, not a git icon — so no icon had rendered for some time, and the
`Contains(FleetGlyphs.Branch)` assertions passed vacuously against `""`. The
nerd-font codepoints (U+E0A0, U+E0B6, U+E0B4) are pinned by a test asserting the
exact escape, which is the only cheap guard against an editor or tool that
silently drops them.

**The dashboard refreshes itself** every `DashboardRefresh.Interval` (4 s) from a
second `AddTimeout`, skipping while `busy`, while a pull is running, or while an
action is queued, and never overlapping itself. Repository selection is now
clamped and restored across a refresh the same way the agent list already was —
without that, an automatic refresh would yank the cursor back to the first row
every few seconds.

The hint bar's buttons keep `CanFocus = false` (focusable buttons join the Tab
order and steal focus from the list) but dropped `NoDecorations`, so they render
bracketed on a raised `fleet.chip` surface. They are laid out with
`Pos.Right(previous) + 1` instead of a manual character offset, which lets
`Dim.Auto` own their widths.

## `wezterm cli` auto-start, 2026-08-10

`fleet doctor` appeared to hang: it printed its whole report, then the shell never
came back. It was not fleet — the process had already exited. `wezterm cli`
**starts a mux server of its own** when it cannot reach the socket, and that
daemon inherits the handles of the pipe the caller is reading, so the reader never
sees EOF. Run detached with output to files it exited in 5 s; run through a pipe it
hung indefinitely. Every invocation also left behind three processes:
`wezterm-mux-server.exe`, an `OpenConsole.exe`, and a default `pwsh -NoLogo` pane,
because an auto-started server opens its default pane. The socket-candidate probe
in `WezTermCli.SocketAsync` tries several paths, so one doctor run per candidate
became one daemon per candidate — 18 servers and 54 processes had accumulated.

Every call now goes through `WezTermCli.Argv`, which inserts `--no-auto-start`
after `cli`. Auto-start is never what fleet wants: if no mux is reachable the
correct outcome is a clean "mux not reachable", not a new daemon. The flag also
makes probing instant — a dead candidate now fails in 0 s instead of burning the
5 s timeout, so the piped doctor run went from >120 s to 0 s.

Worth remembering: "the process hangs" and "the pipe never closes" look identical
from a shell. Checking whether the process still exists (it did not) is what
separated them; comparing a file-redirected run against a piped run confirmed it.

## The picker becomes a cheat-sheet, 2026-08-10

Picker rows are now three columns: the key in blue, a one-word keyword, and the
description as a `FleetRow.Trailing` span. `FleetRowSource.Render` right-aligns
trailing spans by padding to `width - tail`, so descriptions hug the right edge
and reflow on resize — the row cannot know the pane width when it is built, so
the alignment has to happen at draw time, not in the row builder.

Choices are `PickerEntry(Label, Detail)`. Keys are still derived from the label,
which is why the keyword column matters: `AgentDisposal.Entries` labelled
opens/hide/stop/forget/delete yields `o h s f d`, every key a mnemonic of its
word, where deriving from the sentences gave the meaningless `c h s r e`.

**Pressing the key needed a second Enter** because the picker listened on
`window.KeyDown`, and a focused `ListView` eats printable keys for type-to-search
before that fires — the same trap as the dashboard's `n`. It now listens on
`app.Keyboard.KeyDown`.

That introduces a nesting problem: pickers open pickers (manage → default branch),
every one subscribed to the same app-level event, so an inner selection would also
match an outer picker's key and stop the wrong window. Guarded with a static depth
counter — each `Choose` claims `++depth` and its handler ignores keys unless
`depth` still equals its own. `View.IsCurrentTop` looked like the intended answer
but its exact semantics under nested `Run` calls were not worth guessing.

## The prefix moves to Ctrl+Enter, 2026-08-10

`Ctrl+Space` collides with Neovim, and because the WezTerm binding consumes the
chord before the pane sees it, nvim could never get the key back. `Ctrl+D` was the
other candidate and is worse: nvim's half-page scroll, a shell EOF, and already
fleet's own `PageDown`. The default is now `Ctrl+Enter`, which `WezTermChord` maps
to `key = 'Enter', mods = 'CTRL'`.

Changing a default is not enough on an existing install: a saved `keybinds.json`
pinned `"prefix": "Ctrl+Space"` explicitly, and an explicit value always wins over
the defaults. `JsonKeymapStore.Save` now writes the prefix only when it differs
from the shipped one (`KeymapDiff.PrefixAgainstDefault`), the same diffing the
bindings already had — and the existing file needed its prefix blanked by hand.
`apply-keybinds` has to be re-run and the WezTerm config reloaded before the new
chord exists in the terminal.

Keybinds also moved from `k` to `e`, because `k` is move-up: in the fleet menu,
navigating up opened the keybinds editor.

## One pill per row, 2026-08-10

The branch and its status are one pill now: `⟨branch  ⎇ ↓4 ↑1 ●⟩`, built by
`BranchStatus.Pill(branch, state)`. Repositories use the same pill for their
default branch. Agent rows are pill-then-repository, and because pill widths vary
the alignment gap is computed from the widest pill rather than by padding a
column — `PadRight` cannot align a run of coloured spans.

Pull and remove left the repository hint bar for its manage menu (`b branch`,
`p pull`, `r remove`), and their bare keys were dropped from the repository scope
so `d` can no longer drop a repository by accident. The menu runs synchronously
inside `busy`, but pulling wants the spinner and removing wants the confirm
dialog, both owned by the dashboard — so `ManageRepository` returns
`RepositoryManaged(Status, Follow)` and the dashboard performs the follow-up
action itself.

The fleet menu is centred (`FleetTheme.CenteredRows`, sized from its own rows) and
its keys are blue, like the picker. The project picker got the same treatment: name
left, root right-aligned as a trailing span, and chip buttons instead of a hint
line.

## Unregistering a project, 2026-08-10

`d` in the project picker drops a project from fleet and touches nothing else:
`RemoveProjectHandler` refuses a nameless project and one fleet does not know,
then calls `IProjectStore.Remove`, which deletes only the project's json record.
The confirm dialog says so in as many words — "fleet forgets this project.
`<root>` and everything in it stays on disk" — because a "remove" next to a path
reads as a delete, and this one never is. The agent records under the project's
config directory are left alone too, so re-adding the same root brings them back.

The picker rebuilds its rows from the store after a removal rather than mutating
the row list, and clamps the selection, which is the same shape the dashboard uses
for its refresh.

## One owner for the keys, 2026-08-10

The fleet menu's keys needed a second Enter for keybinds (`e`) and the dashboard
(`m`), but not for list agents (`l`) — and that exception was the tell. `l` is
`OpenProject`, which `FleetKeys.ApplyOpen` had bound to `Command.Accept` on the
list, so it was not selecting "list agents" at all: it was accepting whatever row
happened to be highlighted. The other keys went to `list.KeyDown`, which a focused
`ListView` never reaches for printable letters. The menu now listens on
`app.Keyboard.KeyDown` and no longer binds an accept key.

Moving views to app-level keys creates the real problem this section is about: the
handlers of every view *underneath* are still subscribed. An `esc` in the agent
list would also close the menu below it; a `y` in a confirm dialog could match a
key in the picker beneath. `FleetModal` replaces the picker's private counter with
one shared depth: a view claims `Enter()`, ignores keys unless `Owns(claim)`, and
`Leave()`s in its `finally`. Views that keep view-level handlers still claim, so
whatever sits below them goes quiet — `FleetDialog`, `EditKeybindsView`,
`ListAgentsView` — and the dashboard now skips keys when `FleetModal.Any`, which is
the same protection its `busy` flag gave for the modals it opens itself.

`Q` in the menu confirms before quitting: it closes the dashboard and every agent
pane, so the dialog says exactly that and that nothing on disk changes. "Go to the
main pane" is now "Go to the fleet dashboard", which is what it actually does.

## Spacing, caps and chips, 2026-08-10

A `ListView` has no row height, so vertical space between items can only be a real
row. `FleetRowSource` now interleaves a spacer row (`null`) between items and keeps
`Stride`, `ItemAt` and `IndexOf` so a *row* index and an *item* index stay
distinct — every call site went through `FleetRows.Selected/Select/Fill` rather
than reading `SelectedItem` directly, because a raw `SelectedItem` is now double
the data index and silently off-by-one-item wherever it leaks. `FleetTheme.Rows`
wires `FleetRows.KeepOffSpacers`, which bounces a selection that lands on a spacer
onward in the direction of travel, so `j`/`k` and the arrows skip the gaps.

The selection bar is rounded by drawing the powerline caps in the *bar's* colour
against the row background:  at column 0,  at the last column, `Focus`
between them. The cap column is reserved on unselected rows too, otherwise a
right-aligned description shifted a column as the cursor arrived.

The hint bar is no longer `Button`s. Rounded chips need three colours per chip —
edge, key, label — and a Button draws its whole text with one attribute, so
`FleetActionBar` is a small `View` that draws `FleetSpan`s in `OnDrawingContent`
and hit-tests clicks by x range in `OnMouseEvent`. It still never takes focus, so
the list keeps it. Terminal.Gui v2 names that argument type `Mouse`, not
`MouseEventArgs`, and its `Position` is a `Point?`.

Repository rows now lead with the branch pill and follow with the name, matching
the agent rows, and the agents tab says `n add` rather than `n new`.

## The log viewer, 2026-08-10

`L log` lives in the fleet menu, not on the dashboard's tabs — it is a
per-project view, not a per-tab action, and both bars were carrying the same chip.
It opens the project's log: rows of `timestamp   message`, newest first, with a
muted "n more" marker where an entry has detail, and Enter opens that detail.

The key is `L`, not `l`: `l` is next-tab in the dashboard, and taking it for logs
would have silently killed `h`/`l` tab movement.

The log file is one global file, so "this project's log" needed a convention rather
than a second file: `LogTag.For(project, message)` writes `[project] message`, and
`LogParser` splits the tag back out. The viewer keeps entries tagged for this
project *and* untagged ones — untagged means fleet's own failure, which is worth
seeing from anywhere — and drops other projects'. Dashboard outcomes now go through
`Noted`, which logs the same status string the view shows, so the log is a history
of what the dashboard did rather than only what crashed.

Details come from continuation lines: a line without a leading timestamp belongs to
the entry above it. `FileLog.Swallowed` now appends up to twelve indented stack
frames that way, so a swallowed exception has something to open.

Row spacing was tried and dropped. One terminal row is the smallest vertical unit
there is, so "a little space" between rows is not representable — the choice is a
whole blank line or none, and a whole line read as too much everywhere, menus and
pickers included. `FleetRowSource` keeps the capability (`spaced: true`) and the
row/item index mapping that goes with it, but nothing asks for it now; separation
comes from the rounded selection bar instead.

## Two more redraw and focus fixes, 2026-08-10

**Two `update-status` handlers cannot share one status slot.** The pill showed
`default` because `~/.wezterm/tmux-mode.lua` — the user's own tab-bar config — sets
the left status to the workspace name on every tick. Writing ours every tick made the
two alternate once a second, which is the "flicker"; writing ours only when *our*
text changed let theirs overwrite it permanently, which is the `default`. Neither is
fixable from fleet's side, because "unchanged since I last wrote it" is not the same
as "still on screen".

So fleet stopped writing the left status and exports the lookup instead: `M.project(window)`
returns the project of a dashboard in that window, `M.label(window)` prefixes the ship
glyph, and the recipe for a host config is a comment at the top of the module. The
host's pill now reads `fleet.label(win) or win:active_workspace()`, which keeps their
styling, keeps the workspace name in windows without a dashboard, and leaves exactly
one writer. Their config already had this shape for tabs (`fleet.setup(config, { tabs = false })`),
where fleet supplies glyphs and tmux-mode renders them.

Diagnosis note: the wezterm GUI log (`~/.local/share/wezterm/wezterm-gui.exe-log-*.txt`)
is where this became obvious — it was full of `format-tab-title` errors from the same
file, which is what pointed at a second config owning the bar. The `git_branch(cwd_path(pane))`
call in that handler is also what drew the branch bubble that the `-C` fix silenced.

The confirm dialog's buttons move with `h`/`l` and the arrows, and Enter acts on
whichever has focus. That needed the confirm button to stop being `IsDefault` —
a default button answers Enter from anywhere, so "enter selects" and "the focused
button wins" cannot both hold while one exists. `y` and `n`/`esc` still work.

## The tab bar flicker was fleet's git children, 2026-08-10

A branch bubble appeared in the tab bar's right corner for a fraction of a second
every four seconds. Nothing in fleet sets a right status — the user's own wezterm
config does, from the active pane's working directory. The four-second beat gave it
away: it is the dashboard's auto-refresh, and `GitRunner` was starting every `git`
with `WorkingDirectory = workDir`. WezTerm reads a pane's cwd from its foreground
process, so for the ~100 ms each `git status` lived, the pane looked like it had
moved into that repository, and the status handler dutifully drew its branch.

`GitRunner.Argv` now passes the directory as `git -C <dir>` and leaves the child's
cwd alone, so a refresh is invisible to the terminal. `-C` is equivalent for every
call site — git resolves relative paths in the arguments against it exactly as it
did against the process directory — and an empty directory means no flag, which is
what `doctor`'s `git --version` wants.

Worth remembering: a spawned child's working directory is observable from outside
the process. Anything that inspects a terminal pane sees it.

## The tab bar says what things are, 2026-08-10

WezTerm's left segment was `default` — its workspace name, which tells you nothing.
The generated module's `update-status` handler now calls `window:set_left_status`
with a ship glyph and the project of whichever fleet dashboard lives in that window,
falling back to the workspace name when there is none. `fleet_project` was already
written for the prefix chord, so the lookup was there to reuse.

It is drawn as the same lavender bubble wezterm used for `default`: the powerline
caps carry `Foreground = Lavender` over the tab bar's own background while the body
inverts to `Crust` on `Lavender`. A left status is plain text unless you build the
bubble yourself — wezterm's workspace indicator styling is not exposed.

The project tab is titled `fleet` and agent tabs are titled after their branch
(`BranchSlug.Of`) rather than `repo/branch`. A task summary would be better than a
branch name, but nothing records what an agent is working on, so the branch is the
best signal that exists today.

**Titles were being set and then lost.** Agent tabs read `node.exe` because
hide/show moves the pane with `move-pane-to-new-tab`, and the new tab has no title,
so wezterm falls back to the process name. `HideAgentHandler` re-applies the title
after every move.

**The project picker's `d` did nothing** — the same `ListView` type-to-search trap
that had already bitten the dashboard's `n` and the picker's keys: the handler was on
`list.KeyDown`, which printable letters never reach. It now listens on
`app.Keyboard.KeyDown` behind a `FleetModal` claim. Worth noting the pattern: every
time a view keeps its keys on the list instead of the app, this bug comes back.
Removal itself was fine — `JsonProjectStore.Remove` deletes one json record and
nothing else.

## Hide gets a key again, 2026-08-10

Hide is back as a bare key on the agents tab, bound to `x` rather than the `h` its
label suggests: `h` is prev-tab, and hjkl tab movement outranks a mnemonic. It stays
in the manage menu too — the chip is a shortcut, not a move.

## Wrapping navigation, 2026-08-10

`j` at the bottom now goes to the first row, `k` at the top to the last. The wrap
could not live in `FleetKeys`: `View.AddCommand` is protected, so nothing outside a
subclass can replace a `ListView`'s `Command.Down`. `FleetList : ListView` overrides
both commands in its constructor, which also means the arrow keys wrap identically —
they resolve to the same commands — and spacer rows are stepped over on the wrap.

## Opening something already open elsewhere, 2026-08-11

Enter on a repository did nothing and said nothing. `wezterm cli list` through the
GUI socket showed why: `frontend/develop` already had a pane, in window 9, while the
dashboard was in window 10. Both open handlers found that pane and called
`activate-pane`, which activates a pane *within its own window* and cannot raise a
different GUI window — so the call succeeded, nothing moved, and there was no error
to report.

Opening now moves a stray pane into the dashboard's window first (the same
`move-pane-to-new-tab --window-id` that hide/show uses) and re-applies its title,
because a moved pane lands in a fresh tab with none. Spawning a second pane in the
same worktree would have been the other option and a worse one: two editors in one
worktree fight over swap files.

Diagnosis note: the mux sockets live in `~/.local/share/wezterm/gui-sock-*`, and each
GUI has its own. Querying each in turn is how to see the whole picture from outside a
pane — `wezterm cli list` with no socket set only ever shows the mux you happen to be
in, which is why this looked like nothing at all was happening.

## A rebound key reaches the dashboard, 2026-08-11

The dashboard built its `Keymap` once at startup, so a rebind left its chips and its
key handling on the old map — and the editor can run in a *separate* `fleet menu`
process, where no in-process callback could have told it. The dashboard now keeps a
mutable keymap: `EditKeybinds` returns the new one, and the four-second beat reloads
from disk for edits made elsewhere. Adopting one rebuilds the prefix recogniser, the
list motions and the bar.

Reloading needed a cheap "did anything change" test, since rebuilding on every beat
would fight the auto-refresh. `KeymapConfig` holds a dictionary, so record equality
does not do it; `Keymap.Signature` renders the prefix and the sorted bindings into a
string instead.

## Quit remembers, open restores, 2026-08-11

Quitting now records which agents had a live pane — `QuitProjectHandler` compares
each agent's worktree against the pane list *before* killing anything and writes
`Open` back to the record, only when it changed, so a quit does not rewrite every
file. Opening a project then spawns a pane for each agent marked open, skipping any
whose worktree has vanished or that is somehow already running, and a hidden agent
comes back into the hidden workspace rather than the dashboard's window.

`Open` lives on `AgentRecord` beside `Hidden`, which is the same kind of thing: not
configuration, but the state fleet needs to put the desk back the way you left it.

Quit is not the only writer, because a crash never reaches it. Every transition
records itself as it happens: a new agent is born `Open: true`, opening or restarting
one sets it, stopping one clears it, and hiding writes whether a pane was actually
found. Each of those writes only when the flag changes, so the file is not rewritten
on every action.

**The flag was invisible for a while.** `AgentRecord` had it, but `AgentEntry` — the
JSON shape it is mapped to by hand — did not, so every save dropped it and every load
returned `false`. The handler tests passed throughout because they use a fake store;
the one seam that mattered had no coverage. There is now a round-trip test through the
real `JsonAgentStore` asserting both `hidden` and `open` survive. Any new field on a
record needs the same three edits: the record, the entry, and both directions of the
mapping.

## Secrets that git does not carry, 2026-08-11

A repository often needs files that are deliberately untracked — `.env`,
`appsettings.Local.json` — and every worktree needs its own copy. They live in a
mirror under the project root: `.config/{repository}/{defaultBranch}/…`, laid out
exactly as they sit inside the repository, so a file's place in the mirror is its
place in the worktree. `SecretsMirror` is in `Shared` rather than in a slice because
both the repositories manage menu and agent creation need it, and slices may not
reference each other.

Creating a worktree seeds it: `NewAgentHandler` copies the mirror in right after
`worktree add`, only when it actually created the worktree. `m` → `secrets` on a
repository lists what the mirror holds, opens it in nvim for editing, or copies it
into every existing worktree on demand.

**fleet never reads these files.** It enumerates names, and copies bytes with
`File.Copy` — nothing loads their contents into memory, into the log, or onto the
screen. The manage view shows paths and counts only.

`.config` is not a bare repository, so it cannot appear on the repositories tab.

## Opening a project lands on the main pane, 2026-08-11

Restoring a session spawns panes, and each spawn takes focus, so opening a project
with agents left the user staring at whichever agent happened to come last. Focus is
re-asserted on the dashboard pane after the restore, which also activates its tab.

## Installing without a toolchain, 2026-08-11

Three pieces, in the order they matter.

**CI publishes the binaries.** `.github/workflows/ci.yml` builds and tests on
windows-latest and ubuntu-latest, publishes NativeAOT for each RID and *runs the
result* — `fleet --help` must exit 0, which is what turns "Linux should work" into a
fact rather than a hope. Linux needs `clang` and `zlib1g-dev` installed on the runner
for the AOT link. `release.yml` does the same on a `v*` tag and attaches
`fleet-win-x64.exe` / `fleet-linux-x64` with sha256 sums to the release.

**`fleet setup` owns the wiring.** It writes the Lua module to the place that
platform keeps it (`~/.wezterm` on Windows, `~/.config/wezterm` elsewhere), adds the
two `require` lines to the WezTerm config, and prints a checklist of wezterm, git,
nvim and claude with the exact command that fixes each miss. Only wezterm and git
block: an agent can still run Claude Code alone. The glyph check prints a real pill
so a missing Nerd Font is visible rather than described.

Two things that had to be got right. The block goes in **before the final
`return config`** — appending it after would be dead code, and the config would look
wired while doing nothing. And "already wired" cannot be a search for one exact
string: the first version looked for `require 'fleet'` and missed
`pcall(require, "fleet")`, so it cheerfully appended a second copy to a config that
already had one. It now treats any uncommented line mentioning both `require` and
`fleet` as wired. The installer backs the file up as `.bak-fleet` before writing.

**Bootstrappers download instead of building.** `scripts/get-fleet.ps1` and
`scripts/get-fleet.sh` fetch a release asset, put it on `PATH` and run `fleet setup`;
`install.ps1` and `install.sh` still build from source for development. The release
repository is deliberately not baked in — it comes from `-Repo`/`FLEET_REPO`, and the
scripts refuse with an explanation rather than guessing a URL.

**On Linux, "put it on `PATH`" means editing a startup file.** At first the scripts
only printed an `export PATH=...` hint. On Arch, which doesn't put `~/.local/bin`
on `PATH`, that left `fleet: command not found`, and it broke `fleet attach --ssh`,
whose `ssh <host> fleet bridge` runs in a non-interactive shell. So the installers
now add one marked line (`# added by fleet installer`) to a file that shell
actually reads. For bash that is `~/.bashrc`, and the line goes at the **top**:
Arch's stock `.bashrc` starts with `[[ $- != *i* ]] && return`, so an appended line
never runs for an ssh command. zsh gets `~/.zshenv` rather than `~/.zshrc` for the
same reason, fish gets `conf.d/fleet.fish`, and any other shell gets `~/.profile`. The line
checks `PATH` itself, so nested shells don't stack copies. The marker makes the edit
idempotent and lets `install.sh --uninstall` remove exactly that line.
`--no-path`/`FLEET_NO_PATH=1` keeps the old hint.

## A machine that is missing things, 2026-08-11

Tested by stripping `PATH` down to system directories plus git and running the real
binary. What it showed, and what changed as a result:

- `fleet setup` was already right: wezterm, nvim and claude marked missing, each with
  the winget or npm line that fixes it, exit code 1 because wezterm is required.
- `fleet doctor` said `mux reachable no` without ever saying wezterm was absent. It
  now lists wezterm alongside nvim and claude as tools with a found/NOT FOUND state.
- The worst message was the mux one. With no wezterm and no tmux, driver selection
  falls through to `embedded`, so the user was told "the 'embedded' driver is not
  implemented yet" — true, irrelevant, and unactionable. `MuxTrouble.With` now
  distinguishes the two cases: no wezterm on `PATH` means "wezterm is not installed,
  or not on PATH. Install it and run 'fleet setup'." The driver message survives only
  for the case it describes, being inside tmux with wezterm present.
- `fleet` drew the whole project picker *before* checking the mux, so the failure
  arrived after the user had chosen. The check moved above the picker.
- `fleet quit` reported "Nothing of this project is open", which is what an empty pane
  list looks like whether or not a multiplexer exists. It now names the real problem.
- A missing **harness** used to surface as "the wezterm multiplexer did not respond"
  when the spawn of a nonexistent `nvim` failed. Creating or opening an agent now
  refuses with `nvim is not on PATH. Install it, or change what this agent opens.`
  Session restore skips agents whose harness is absent and says so on stderr, rather
  than filling the window with panes that die on arrival.

The environment checks live in the composition root, not in the handlers: the
handlers stay ignorant of `PATH` and take probes, which is why all of this is
testable without a machine that lacks anything.

## Installing the dependencies too, 2026-08-11

`-WithDeps` / `--with-deps` turns the installers into machine setup: WezTerm, Neovim
and git through winget on Windows, through pacman/dnf/apt on Linux, then a Neovim
config cloned into the right place for the platform.

Three decisions worth keeping:

- **Nothing is overwritten.** A tool already on `PATH` is skipped. A config directory
  that is a checkout of the same remote is fast-forwarded; one with a *different*
  remote, or one that is not a checkout at all, is reported and left exactly as it
  was. A Neovim config is somebody's work, and an installer that clobbers it is worse
  than one that does nothing.
- **The config's own `bootstrap.sh` is never run.** It is reported. Installing a
  package is one kind of consent; executing a script from a repository is another.
- **winget's PATH changes do not reach the running process**, so after installing,
  the usual locations (`Program Files\WezTerm`, `Neovim\bin`, `Git\cmd`) are prepended
  to this process's `PATH`. Without that, `fleet setup` at the end of the same run
  would report the tool it just installed as missing.

The logic lives once. `scripts/deps.ps1` defines a function and does nothing on
dot-source, so `install.ps1` sources it locally and `get-fleet.ps1` downloads it from
the same repository — which is what makes `-WithDeps` work through `irm | iex`. On
Linux the same trick uses `install.sh --deps-only`.

The default config URL is a real personal repository rather than a placeholder,
because a placeholder in an installer is a broken installer. It sits on one line at
the top of each script, and both honour `FLEET_NVIM_CONFIG`.

## yazi as the folder picker, 2026-08-14

Typing `n` in the New project form did nothing but reopen the form. Four views ran a
window without claiming the keys — `CreateProjectView`, `AddRepositoryView`,
`NewAgentView`, `CaptureKeyView` — so the project picker underneath, which listens on
`app.Keyboard.KeyDown`, kept firing its `n`/`d`/`q` bindings on every letter typed
into a field. The two repository and agent forms only escaped it because the dashboard
guards with `busy`. All four claim `FleetModal` now, and a test walks `src` for any
file that runs `app.Run(window)` without claiming, so the next view cannot forget.

The folder picker uses `yazi --cwd-file <tmp>`: yazi writes the directory it ended up
in when it quits, which is exactly "pick a folder" rather than "pick a file". fleet
spawns it in its own tab, focuses it, and then *blocks* its own loop polling for the
file — acceptable only because the user is looking at yazi in another tab, and the
poll also watches for the pane disappearing, so killing the tab returns immediately
instead of waiting out the ten-minute ceiling.

`FileBrowser` holds the parts worth testing: the argv for both modes, reading the
first line of the cwd file, and where to start — the typed path if it exists, else its
nearest existing parent, else home. Half a path is the normal case when someone is
mid-type, and starting at home there would throw away what they had written.

`f` in the fleet menu opens the same navigator in the project root, from the menu and
from the dashboard both. yazi is an optional dependency: without it the browse button
hides itself and the menu entry reports what to install, which is why `fleet setup`
lists it as costing "no folder picker or file navigator" rather than blocking.

## The `embedded` driver, Phase 0 spike, 2026-09-24

Branch `feat/embedded-mux`, code in `spikes/EmbeddedSpike`. The question was whether
fleet can own one pane end to end, from the PTY through a real terminal emulator
to the host terminal, with a prefix key that works while nvim or claude has focus.
This goes further than the attach model above ("no terminal emulator"): with an
emulator in the path, repaint on attach becomes an exact screen dump rather than
a ring-buffer replay, which is the upgrade *Repaint on attach* named.

**Verdict: go.** libghostty-vt from .NET NativeAOT works on Windows and Linux,
statically linked, with no warnings. Everything the spike set out to prove was
observed working, not just compiled. The one design change it forces is on the
Windows input path; see *Keys on Windows go to ConPTY as records* below.

### What was built

One NativeAOT console app with one pane:

```
host console/tty --records/bytes--> prefix check --> ConPTY | pty master
       ^                                                    |
       |                                                    v
  diffed render <-- render state <-- libghostty-vt <-- PTY output
```

- **PTY.** Windows uses ConPTY through `RoyalApps.RoyalTerminal.Terminal.Pty.Windows`
  0.5.0, as the 2026-08-08 entry recommended. Only the Windows package is
  referenced, because the `Platform` package also pulls in the Unix one, which
  P/Invokes `forkpty` and returns into managed code in the child. Linux uses
  hand-written `openpty` + `posix_spawnp` (see the next point).
- **A controlling terminal without fork.** The PTY section above says
  `posix_spawn` cannot issue `TIOCSCTTY`. It does not need to. With
  `POSIX_SPAWN_SETSID` plus a file action that opens `/dev/pts/N` as fd 0, glibc
  runs `setsid` and then `open` in the child, and a session leader with no
  controlling terminal acquires the tty it opens. Verified: nvim runs, gets
  `SIGWINCH` on resize, and redraws. Signal dispositions and the mask are reset
  with `SETSIGDEF`/`SETSIGMASK`. This is glibc-specific (macOS numbers the flag
  differently) and removes the need for a helper binary.
- **Emulator.** One libghostty-vt terminal fed from the PTY. Its `write_pty`
  callback sends terminal replies (DSR, DA, mode reports) back to the child. A
  DA1/DA2 callback answers as a VT220-class terminal. Without it, nvim waits
  out its query timeout.
- **Render.** The render-state API (`ghostty_render_state_*`) gives dirty rows.
  Each dirty row is read cell by cell and diffed against a shadow of what the
  host already shows, and only changed cells are written, wrapped in
  synchronized output (`?2026`). Palette colours stay palette indices, so the
  host theme still applies. RGB stays RGB, and "no colour" becomes SGR 39/49.
  Wide characters and their spacer tails are handled. On Windows the console
  code page is set to UTF-8 (65001) and restored on exit, the 2026-08-08 fix.
- **Input, Windows.** `ReadConsoleInputW` records with
  `ENABLE_VIRTUAL_TERMINAL_INPUT` off, as herdr does. The prefix is decided on
  the virtual key and modifier state, so whatever mode the pane has put the
  terminal in cannot change what the prefix looks like. This retires the
  2026-08-08 finding that win32-input-mode broke a byte-level scanner.
- **Input, Linux.** Raw stdin bytes with a byte-level prefix check (Ctrl+B is
  `0x02`), passed through otherwise. Good enough for the spike. Phase 1 needs a
  host input parser here too (see what did not work).
- **Prefix.** `Ctrl+B` by default (`--prefix ctrl+<letter>`). `prefix q` quits,
  `prefix prefix` sends the chord through, `prefix d` dumps the screen, and
  `prefix r` redraws. Unbound keys after the prefix are swallowed, as in tmux.
  While the prefix is armed, a badge shows top right.
- **Resize.** Host → emulator (`ghostty_terminal_resize`) → PTY. Windows sees it
  from `WINDOW_BUFFER_SIZE_EVENT` plus polling, Linux from `SIGWINCH` via
  `PosixSignalRegistration` plus `TIOCGWINSZ`.
- **Restore.** On exit: SGR reset, mouse modes 1000/1002/1003/1006, focus 1004,
  bracketed paste 2004, DECCKM, keypad mode, cursor shape and visibility, then
  leave the alt screen. Console modes and code pages are restored on Windows,
  termios on Linux.
- **Test hooks.** `--dump FILE` keeps the emulator's screen as plain text (via
  `ghostty_formatter`) with size and counters. `--log FILE` records every key
  record and the bytes sent. `--inject PID tokens…` attaches to the spike's
  console and writes real `INPUT_RECORD`s with `WriteConsoleInputW`, so the
  Windows tests go through the same `ReadConsoleInputW` path as a keyboard.

### libghostty-vt: pin and build

| | |
|---|---|
| Source | `ghostty-org/ghostty` @ `44f2a44df7e8c4a0c6df3f7d872ef3d7ead88e51` (reports `1.3.2-HEAD`). The same commit herdr vendors, so it is known-good on Windows MSVC |
| Toolchain | Zig **0.16.0** (`build.zig.zon` requires it) |
| Pins | `spikes/EmbeddedSpike/native/pins.env`: Zig version, the SHA-256 of both Zig archives, the ghostty commit and the SHA-256 of its GitHub tarball |
| Windows | `native/build.ps1` → `zig build -Demit-lib-vt -Doptimize=ReleaseFast -Dsimd=true -Dtarget=x86_64-windows-msvc` → `ghostty-vt-static.lib` (12 MB) |
| Linux | `native/build.sh` → same flags with `-Dtarget=x86_64-linux-gnu` → `libghostty-vt.a` (18 MB) |
| Linking | `<DirectPInvoke Include="ghostty-vt" />` plus `<NativeLibrary>` pointing at the archive, and `ntdll.lib` on Windows. Every `[LibraryImport("ghostty-vt")]` becomes a direct call resolved at link time, so there is no DLL at runtime |
| Result | `embeddedspike.exe` 3.99 MB. Linux `embeddedspike` 4.18 MB, depending only on `libc`/`libm`. Zero IL, trim, AOT or link warnings with `TreatWarningsAsErrors` |

The scripts download Zig and the source into `native/.cache`, check both hashes,
and install nothing system-wide, so a CI runner needs only what NativeAOT already
needs: MSVC and the Windows SDK on Windows, `clang` on Linux. Zig fetches
ghostty's own dependencies at build time; `build.zig.zon` pins their hashes. A
cold build takes minutes, so CI should cache `native/.cache`.

One Zig 0.16 trap on Windows: with `ZIG_GLOBAL_CACHE_DIR` pointing at a fresh
directory, the dependency fetch fails with `FileNotFound` unless `<cache>/p`
already exists. Both scripts create it.

The bindings are hand-written from the C headers
(`Ghostty/Native.cs`), about 45 functions and the struct layouts they need. No
generator was used. The headers are the ABI, and the render and formatter
structs carry a `size` field, so a mismatch shows up as an error code rather
than a crash. Alternatives seen along the way, not used: RoyalApps ships
`RoyalApps.RoyalTerminal.GhosttySharp` with a prebuilt dynamic `ghostty-vt.dll`,
and `DeBlasis.GhosttyVt` (Parked). Both trade the Zig build for someone else's
pin and a DLL next to the binary.

**XtermSharp was not evaluated.** It was the fallback in case libghostty-vt
turned out impractical from .NET, and it did not: Zig, MSVC linking and AOT all
worked on the first try. The reasons for the pick are fidelity, since this is
Ghostty's own parser, screen and key encoder with kitty keyboard, mouse modes
and grapheme handling, and one pinned upstream on both OSes.

### Credit: herdr

[herdr](https://github.com/ogulcancelik/herdr) (Apache-2.0) is a Rust terminal
multiplexer built on libghostty-vt that ships on Windows. This spike takes from
it: the Zig invocation and target triples from its `build.rs`, the ghostty pin,
the Windows input rules in `Input/WindowsKeys.cs` (AltGr as `LEFT_CTRL|RIGHT_ALT`
is text, not a chord; modifier-only records produce nothing; Alt+numpad codes
arrive as a `VK_MENU` key-up carrying the character; surrogate pairs span two
records; `vk == 0` is synthesized text), and the Windows checklist below, taken
from its CHANGELOG. The files that port its logic say so in a comment.

### Keys on Windows go to ConPTY as records

The spike first encoded every key with libghostty-vt's key encoder, which honours
the pane's modes, and that broke in a way that matters. Claude Code turns on the
kitty keyboard protocol, so the encoder sent Ctrl+U as `CSI 117;5u`, **and Claude
did not clear its prompt.** The child never sees our bytes directly. ConPTY
parses them back into console records, and ConPTY does not understand kitty's
CSI-u.

ConPTY says what it wants. The first bytes of every session are
`ESC[?9001h ESC[?1004h`, a request for win32-input-mode and focus events. The
spike already holds the `KEY_EVENT_RECORD`, so when 9001 is on it forwards the
record verbatim as `ESC[Vk;Sc;Uc;Kd;Cs;Rc_`, the spec Windows Terminal
implements. ConPTY rebuilds the identical record for the child, and the child's
own console mode decides the rest. With that, Ctrl+U clears Claude's prompt.
Focus events go through as `CSI I`/`CSI O` when 1004 is on.

Consequences:

- On **Windows**, libghostty-vt's key encoder is the fallback (`--keys ghostty`,
  or a ConPTY that never asks for 9001), not the main path. Records are lossless
  and cheaper.
- On **Linux** the encoder is the right tool. It is what fleet needs to turn
  parsed host input into whatever the pane asked for (kitty flags,
  modifyOtherKeys, DECCKM).
- The prefix check stays on the translated key in both paths.

### Verified

Windows 11, conhost, driven by `--inject` (real console input records) with
`--dump` screen captures:

| Check | Result |
|---|---|
| `nvim --clean file` renders into the grid | ✓ text, `─ ✓ 日本語`, status line at full width |
| typing in insert mode | ✓ |
| `i`, `Ctrl+V`, `prefix prefix` | ✓ nvim inserts a literal `^B`, so the chord reaches the pane |
| `prefix r` redraw, `prefix d` dump, unbound `prefix z` swallowed | ✓ |
| `prefix q` quits and restores the console | ✓ exits cleanly; entry logged mode `0x1F7→0x1A8`, CP `437→65001`. The restore is the saved values written back, not read back afterwards |
| resize by dragging the window (`MoveWindow`) | ✓ 120x30 → 95x22 → 181x41. nvim and claude both re-lay out |
| `claude` trust dialog and main prompt render | ✓ box drawing, logo, status line |
| typing into claude, `Ctrl+U` clears the line | ✓ with win32-input-mode, ✗ with the ghostty encoder |
| `prefix d`, `prefix q` while claude is focused | ✓ |
| nvim with modifyOtherKeys, encoder path | ✓ `|` arrives as `CSI 27;2;124~` and is inserted |

Linux, in `mcr.microsoft.com/dotnet/sdk:10.0` (Debian) under Docker Desktop,
with `script` providing the host pty:

| Check | Result |
|---|---|
| `build.sh` + AOT publish | ✓ from a clean container |
| nvim renders through `openpty` + `posix_spawn` | ✓ |
| typing, `Ctrl+V` `prefix prefix` inserts `^B` | ✓ |
| host pty resized 100x30 → 72x20 | ✓ emulator and nvim both at 72x20 |
| `prefix d`, `prefix q`, restore sequence emitted, exit 0 | ✓ |

Setting `SetConsoleScreenBufferSize` from another process fails with
`ERROR_INVALID_PARAMETER` while the alt screen is active. That is why the resize
test moves the window instead. Worth knowing for any future scripted test.

`dotnet build Fleet.slnx` and `dotnet test` stay green (0 warnings, 926 passed).
The spike is not in the solution, as with the earlier spikes.

### What did not work, or was not done

- **Not tested by the spike:** Windows Terminal, PowerShell as the pane,
  Ghostty/kitty on Linux, claude on Linux (not installed in the container), macOS
  (not built). The manual checklist below covers these. **WezTerm on Windows was
  checked by hand afterwards (2026-09-24): nvim and claude render and take input
  correctly, and the prefix works in both.**
- **Mouse** is not forwarded: `ENABLE_MOUSE_INPUT` is off, and the host is never
  asked for mouse modes. On Windows, mouse records can go to ConPTY the same way
  keys do.
- **Host-side modes** the pane asks for are not mirrored to the host: bracketed
  paste, focus reporting on Linux, the window title (OSC 0/2), clipboard
  (OSC 52), cursor colour, hyperlinks and kitty graphics. On Windows a paste
  arrives as key records, which is correct but not bracketed.
- **Linux input is raw bytes.** A kitty-mode pane on Linux gets legacy keys, and
  the prefix check would misfire on a `0x02` inside a paste. Phase 1 needs a host
  input parser that feeds the key encoder.
- **Underline style** is re-emitted as `4:n`. Terminals without styled
  underlines may show a plain underline or none.
- **Dead keys** are untested. The translator passes whatever character the
  console reports, which is exactly the herdr bug on the checklist.
- **No scrollback view.** Only the active screen is drawn.

### Windows input checklist for later phases

From herdr's CHANGELOG. Each item is a bug herdr shipped and fixed on Windows.
The records path avoids some of them by construction; each needs a test before
`embedded` is called done.

- [x] **Alt combinations.** Alt+letter, Alt+Shift+letter (must not collapse to
      uppercase), Alt+Backspace, Ctrl+Alt+letter (fish decodes these as both
      modifiers). Right Alt is AltGr only with `LEFT_CTRL` also set. *Done 2026-09-28 for `embedded`, see "Windows input checklist".*
- [x] **Ctrl+S** reaches the pane (not taken as XOFF, not claimed by the host;
      WezTerm on this machine claims it as a leader). Also Ctrl+/, Ctrl+1..9 as
      keys rather than control bytes, and Ctrl+J as LF, distinct from Enter. *Done 2026-09-28 for `embedded`, see "Windows input checklist".*
- [x] **Dead keys and AltGr text.** US-International `'` + `e` gives `é` once,
      with no extra base character; AltGr+dead key; non-US shifted text such as
      `@` on German layouts; IME commits; emoji from the Windows picker
      (surrogate pairs, or CSI-u with associated text under WezTerm). *Done 2026-09-28 for `embedded`, see "Windows input checklist".*
- [x] **SGR mouse past column 95.** Coordinates must use SGR (1006) encoding end
      to end. Legacy encodings stop at 95/223. Reattach must restore mouse
      reporting. *Done 2026-09-28 for `embedded`: a click at column 110 reached
      an SGR app intact. See "Mouse".*
- [x] **Pasted Enter.** A multi-line paste from Windows Terminal arrives as key
      records with `VK_RETURN`. It must reach the pane as one bracketed paste with
      its newlines, and must not submit each line (herdr: Codex lost Enter after
      long pastes; OMP/Pi submitted per line). LF-only pastes keep their
      newlines. *Done 2026-09-28 for `embedded`, see "Paste on Windows"; the
      conhost path and the ConPTY (WezTerm/WT) path were verified, not a real
      WezTerm or Windows Terminal window.*
- [x] **Mode restore.** On exit and on detach, reset mouse (1000/1002/1003/1006),
      focus (1004), bracketed paste (2004), cursor keys, keypad, cursor shape and
      visibility, and the alt screen. Restore console modes and code pages even
      on a crash (herdr #4055 still had a Git Bash report open). *Done 2026-09-28 for `embedded`, see "Windows input checklist".*
- [x] **Escape** is sent at once, not held as a possible Alt prefix, and a lone
      Esc beside another key is not fused into an Alt chord. *Done 2026-09-28 for `embedded`, see "Windows input checklist".*
- [x] **Shift+Enter** keeps its modifier. **Shift+Tab** reaches the pane as
      CSI Z; the permission-mode cycle depends on it. *Done 2026-09-28 for `embedded`, see "Windows input checklist".*
- [x] **Key repeat and release** stay with the pane that got the press. *Done 2026-09-28 for `embedded`, see "Windows input checklist".*
- [x] **Incomplete host replies** (split `ESC ]` OSC colour answers) are not
      mistaken for Alt+`]`. *Done 2026-09-28 for `embedded`, see "Windows input checklist".*

### Manual test checklist

For a human, on each host: PowerShell in conhost, Windows Terminal, WezTerm on
Windows, and Ghostty and kitty on Linux. Build with `native/build.ps1` or
`native/build.sh`, then `dotnet publish -c Release -r <rid> -o out/<rid>` in
`spikes/EmbeddedSpike`. Add `--log log.txt` to capture key records when
something looks wrong.

1. **nvim renders.** `embeddedspike -- nvim <some file with unicode>`. Colours
   match the host theme, box drawing and CJK align, the cursor shape changes
   between normal and insert, and scrolling with `Ctrl+D`/`Ctrl+U` has no
   leftovers.
2. **claude renders.** `embeddedspike -- claude`. Logo, prompt box and status
   line are intact. Spinner updates leave no trails. The trust dialog's
   selection highlight moves.
3. **Prefix under nvim.** In insert mode, `Ctrl+V` then `Ctrl+B Ctrl+B` inserts
   `^B`. `Ctrl+B` shows the badge top right. `Ctrl+B d` writes the dump.
   `Ctrl+B q` quits.
4. **Prefix under claude.** Type text, `Ctrl+U` clears it, `Ctrl+B Ctrl+B`
   reaches claude, and `Ctrl+B q` quits even while claude is busy streaming.
5. **Alt, Ctrl+S, Shift+Tab** under claude: Alt+B/F move by word, Shift+Tab
   cycles permission mode, Ctrl+S does what claude does with it.
6. **Resize.** Drag the window smaller and larger, maximise, restore. nvim's
   status line and claude's prompt box follow within a frame and nothing is
   left behind at the old edge.
7. **Restore.** After `Ctrl+B q`: the prompt returns on the normal screen, the
   cursor is visible with its usual shape, typing echoes, mouse clicks do not
   print escape codes, and a paste is not wrapped in `200~`. On Windows,
   `chcp` reports the original code page.
8. **Host specifics.** Windows Terminal: pasting multiple lines into claude does
   not submit each line. WezTerm: note that its own Ctrl+S leader (this machine)
   wins. Ghostty and kitty: the host's kitty keyboard mode is not left on after
   exit.
9. **Project switching (once Phase 1 lands).** Open two projects, each with
   nvim, claude and a sub-orchestrator running. Switch back and forth several
   times: nothing redraws from scratch, scrollback and cursor positions are
   kept, the hidden dashboard keeps polling, and `switch A -> B` in the log
   reads low tens of milliseconds. Attach a second client showing the other
   project; switching in the first never changes the second.

## Remote attach, 2026-09-24

Goal: from a laptop, attach to panes that live on another machine over SSH, as
herdr's `--remote` does. Linux→Linux, Windows→Linux, Linux→Windows and
Windows→Windows all count. This does not need a different design. It needs the
fleetd protocol to follow a few rules from the start, because each rule is cheap
now and expensive to retrofit once clients exist.

### How herdr does it

`herdr --remote host` starts a local thin client and runs
`ssh -T host herdr remote-client-bridge` (herdr `src/remote/attach.rs`). On the
remote, the bridge makes sure the server is running, connects to the server's
**local** client socket, and copies SSH stdin and stdout to and from it
(`src/remote/host.rs`). That is all it does. The client speaks the same protocol
whether the server is local or remote, and SSH is one more byte pipe. Around that
core herdr adds a protocol version check (offering to install or update the
remote binary), strict host-key checking, a managed SSH config with a control
socket, and ssh-agent forwarding.

Its wire format, `src/protocol/wire.rs`: length-prefixed messages. Clients send a
hello with size and cell pixels, then input and resize messages. The server sends
`TerminalFrame { seq, width, height, full, bytes }`, already-diffed escape bytes
the client writes to stdout, plus separate messages for window title, clipboard,
notifications and mouse capture.

### The rules for fleetd's protocol

1. **Byte stream only.** Length-prefixed messages over any duplex stream: a named
   pipe, a Unix socket, or SSH stdio. No passing file descriptors or handles, no
   shared files or memory, and no assumption that client and daemon share an OS
   or a filesystem. `fleet bridge` on the remote side is then a stream copy
   between stdio and the local endpoint.
2. **Versioned handshake.** The client's hello carries the protocol version, its
   OS, its terminal size and what its terminal supports (truecolor, styled
   underline, synchronized output, kitty keyboard). The daemon refuses an
   incompatible version with a message naming both versions, and renders for
   what the client can show.
3. **Input goes over the wire as neutral key events.** A key event is key,
   modifiers, text, and press/repeat/release, plus the raw Windows
   `KEY_EVENT_RECORD` when the client has one. The daemon encodes it for the
   pane:
   - ConPTY pane that asked for win32-input-mode: the raw record if present,
     otherwise one built from the neutral fields;
   - any other pane: libghostty-vt's key encoder under the pane's modes.

   This is the one place the Phase 0 finding (*Keys on Windows go to ConPTY as
   records*) shapes the protocol. A Linux client attaching to a Windows fleetd has
   no records to forward, so the neutral form must be enough on its own. herdr's
   `ClientKeySource::{WindowsConsole, Vt, Synthesized}` makes the same split.
   Mouse, paste and focus are their own messages. A paste is text, never
   keystrokes, so the daemon can bracket it for the pane.
4. **The daemon renders; the client writes bytes.** Frames carry a sequence
   number and a `full` flag. Attach, resize and reconnect get one full frame,
   then diffs. Diffs are small, which is what makes SSH usable, and reconnecting
   after a dropped link is only "send a full frame".
5. **Host effects are messages, not escape sequences inside frames.** Window
   title, clipboard (OSC 52), notifications, bell, and mouse-capture and
   bracketed-paste state all have to act on the machine the user is sitting at.
   The client applies them to its own terminal and restores them on detach.
6. **fleetd outlives the SSH session.** It is already a detached daemon. On
   Windows it must also survive logout of the OpenSSH session that started it,
   and the bridge must start it if it is not running.

Control traffic — the `fleet` CLI verbs, the dashboard, MCP — goes over the same
protocol. That makes remote orchestration a later option at no extra cost,
rather than a second protocol.

### Two ways to reach a remote pane

- **Bridge (the main path).** `fleet attach --ssh host`: the keyboard is read
  locally (console records on Windows, parsed bytes on Linux), and frames come
  back over SSH. Terminal fidelity is the local terminal's.
- **Plain SSH (works for free).** `ssh host`, then `fleet attach` on the remote.
  Input then goes through the remote's terminal layer. On a Windows OpenSSH
  server that is sshd's ConPTY, where herdr's CHANGELOG lists several
  OpenSSH-specific input and mouse bugs. Supported, but not the path to optimise.

### Plan

Folded into the combined plan at the end of *Instant project switching on
`embedded`*, which supersedes the four-step list that was here.

## Instant project switching on `embedded`, 2026-09-24

A core requirement, not later polish. Read together with
`docs/workspace-native-switching-postmortem.md`, whose two WezTerm attempts
failed for reasons this design has to rule out by construction.

### Behaviour

- **Each project is a workspace in fleetd** holding its claude pane, dashboard,
  agent panes and sub-orchestrator browser panes, in their split layout.
- **Switching hides one workspace and shows another.** Nothing is killed,
  spawned, restarted or moved. Every process keeps running while hidden,
  including the dashboard, which keeps polling.
- **It is instant:** one control round trip to fleetd, then the target is drawn
  from screen state that already exists. No program is asked to repaint.
- **Hidden agents use the same mechanism.** A hidden agent lives in its
  project's hidden workspace, which no client is ever told to show. On
  `embedded` this replaces `HiddenNest` and the pane moves in
  `MoveProjectHandler`.
- **Visibility is per client.** Hiding a project in one attached client never
  changes what another client shows. That is exactly what WezTerm could not do:
  its workspace visibility is global to a GUI process (postmortem, attempt 1).
- **Opening a project for the first time creates its workspace.** Quitting a
  project closes its workspace, its hidden workspace and every pane in them, as
  today.

### Why this works on `embedded` when it failed on WezTerm

fleetd owns the whole model, so "hidden" is a fact fleetd records, not a
side effect of some other feature:

```
fleetd
  workspaces   name -> layout tree of pane ids      ("fleet", "fleet~hidden", ...)
  panes        id   -> PTY + libghostty-vt terminal (runs whether shown or not)
  clients      id   -> showing: workspace name, size, focused pane
```

- **Switching** is one message, `Show { workspace }`, from a client. fleetd
  changes that client's `showing`, composites a full frame of the target from
  its panes' emulators, and sends it. That frame is the entire round trip.
  Scrollback and cursor positions survive because they live in each pane's
  emulator, which never stopped. Nothing touches a PTY, so no child notices.
- **Per-client visibility** falls out of the data: `showing` belongs to a
  client, and no operation writes it for any client but the sender. A test
  asserts this.
- **"Instant from existing state"** means fleetd's emulators, not a client-side
  cache. A client-side frame cache per workspace could skip even the round trip,
  but it would have to reconcile anything that changed while hidden. Not worth
  it unless measurement says the round trip is slow.
- **Size.** A hidden workspace keeps its panes at their last size. When it is
  shown in a client of a different size, its panes are resized once, which does
  make those programs redraw. Same-size switches, the common case, redraw
  nothing. When two clients show the same workspace at different sizes, the
  most recently active client's size wins, as in tmux's `latest`.

### The postmortem's structural lesson: decide once, pass it down

The WezTerm attempts broke because each call site re-derived which instance and
which window it was acting on (`DashCommand` cold-starting a second GUI). Here:

- **The client is explicit.** When a client's prefix opens the fleet menu,
  fleetd starts the menu process with `FLEET_CLIENT=<client id>` (and
  `FLEET_PANE`). A switch from that menu sends `Show` for that client and no
  other. A command with no `FLEET_CLIENT` — a hook, cron, MCP, a plain shell —
  may create, close or hide panes but never changes any client's view.
- **The switch strategy is chosen once per driver.** `MenuCommand` stops calling
  `MoveProjectHandler` directly and asks for the driver's switch strategy, so the
  moving approach is no longer hard-coded in the command.

### Port changes

- `MuxCaps.Workspaces`: the driver has real workspaces with per-client
  visibility. `embedded` sets it. `wezterm` does not, and keeps its move-based
  switching (including the `perf/switch-without-kills` behaviour if that merges
  to `main` first; it is not on `main`, so nothing here depends on it).
- `IMuxDriver` gains three operations, which the WezTerm driver implements the
  way it can:
  - `ListWorkspacesAsync()`: each workspace's name and whether the *current
    client* is showing it. WezTerm derives the latter from panes in the current
    window, which is what `ProjectsVisibleInWindow` computes today.
  - `ShowWorkspaceAsync(name)`: on `embedded`, one `Show`. On WezTerm, not
    supported (`Caps` says so); the move strategy is used instead.
  - `CloseWorkspaceAsync(name)`: kills every pane in it.
- A feature-level `IProjectSwitch` with two implementations:
  - `WorkspaceSwitch` for drivers with `MuxCaps.Workspaces`: hide the current
    project and show the target with one `ShowWorkspaceAsync`, creating the
    workspace through `OpenProjectHandler` if the project is not open yet.
  - `MoveSwitch`: today's `MoveProjectHandler.ParkAsync` + `HandleAsync`,
    unchanged.

  `ProjectSwitch.For(driver)` picks between them from `Caps`, in one place.
- **"Is this project open, and where"** moves out of `MenuCommand` into one
  query shared by the switch picker's `(open)` labels, `QuitFleet`'s
  `ProjectsVisibleInWindow` and `FocusMain`: open = its workspace exists (on
  WezTerm: any pane has its root as cwd); visible here = the current client is
  showing it. A hidden project still counts as open.
- `HideAgentHandler` on `embedded` moves the agent's panes into
  `<project>~hidden`, a data-structure change inside fleetd that leaves every
  process alone. `HiddenNest` stays for WezTerm.

### Measuring it

Every switch logs `switch A -> B: N ms` on both drivers, measured in the menu
around the strategy call, as the WezTerm branch does. fleetd also logs its side:
time to composite and send the frame. Target on `embedded`: low tens of
milliseconds end to end. A 200x50 frame is about 10,000 cells; the spike's
per-cell reads run well inside that budget. If it does not, the first lever is
caching each workspace's last composited frame in fleetd.

### Acceptance

Tests, against the fake driver and against fleetd's in-process model:

- a switch never calls kill or spawn (the fake driver records both);
- pane ids, and the fake processes behind them, are identical after hide → show
  → hide;
- two clients show two different projects at once, and a switch by one leaves
  the other's `showing` and last frame unchanged;
- a command without `FLEET_CLIENT` cannot change any client's view;
- hidden agents are not in any shown frame but still receive output;
- quitting a project closes both its workspaces and nothing else.

Manual, added to the checklist below: with nvim, claude and a sub-orchestrator
running in two projects, switch back and forth repeatedly. Nothing redraws from
scratch, scrollback and cursor positions are kept, the dashboard keeps updating
while hidden, and the logged switch times are in the low tens of milliseconds.
In a second attached client showing the other project, nothing changes.

### Cost, stated

This pulls tiling into Phase 1. The earlier design attached one pane,
fullscreen; a workspace with a split layout needs:

- a layout tree per workspace;
- a compositor drawing several pane grids plus borders into one frame;
- focus and input routing to the focused pane;
- mouse hit-testing across panes;
- per-pane PTY sizes derived from the layout.

The spike's renderer is the per-pane half of the compositor. The layout half is
new. It is the largest single item in Phase 1.

### Combined Phase 1 plan

1. **Protocol** in `src/Fleet`, unit-tested: codec, handshake, neutral key
   events with both encoders, `Show`/workspace and client messages, frame
   messages.
2. **fleetd model**: workspaces, layout trees, panes, clients, per-client
   visibility. Pure and in-process first, so the acceptance tests above run
   without a terminal.
3. **Compositor**: the spike's renderer generalised to a layout of panes with
   borders and focus.
4. **fleetd process plus local `fleet attach`**: detach, reattach, exact screen,
   and switching with timing logs.
5. **Port changes**: `MuxCaps.Workspaces`, the three `IMuxDriver` operations,
   `IProjectSwitch`, the shared open/visible query, and `MenuCommand` rewired
   through them. The WezTerm behaviour is unchanged and covered by its existing
   tests.
6. **`fleet bridge` and `fleet attach --ssh`**: Linux→Linux, Windows→Linux,
   Linux→Windows.
7. **`EmbeddedDriver` behind `IMuxDriver`**, then `DriverSelector`.

## Phase 1 status, 2026-09-28

Steps 1–7 of the combined plan are built on `feat/embedded-mux` and run
end to end on Windows and Linux. `DriverSelector` needed no change: it already
falls back to `embedded`, and `FLEET_MUX=embedded` forces it. What changed is
that choosing it now works, because the build carries libghostty-vt.

### Where things are

| Piece | Code |
|---|---|
| Workspace port and switching | `Ports/Mux` (`Workspace`, `MuxCaps.Workspaces`, three operations), `Features/Projects/SwitchProject`, `Features/Projects/LocateProject`, `MenuCommand` |
| fleetd model: workspaces, tabs, split layouts, per-client views and overlays | `Platform/Mux/Embedded/Model` |
| Compositor and diff encoder | `Platform/Mux/Embedded/Render` |
| Wire protocol | `Platform/Mux/Embedded/Protocol` |
| Emulator, PTYs, host consoles, key translation | `Platform/Mux/Embedded/{Native,Pty,Host,Input}` (ported from the spike) |
| The daemon | `Platform/Mux/Embedded/Daemon` |
| Driver and attach client | `Platform/Mux/Embedded/EmbeddedDriver.cs`, `Platform/Mux/Embedded/Client` |
| Commands | `fleet daemon`, `fleet attach [--project p] [--ssh host]`, `fleet bridge` |
| Native build | `scripts/ghostty/build.{ps1,sh}`, into `artifacts/ghostty/<rid>`. `Fleet.csproj` links it when present; CI builds it |

fleetd starts on demand, from `fleet`, `fleet attach` or the driver, and exits
ten seconds after its last pane and client are gone. The client prefix is
Ctrl+B (`FLEET_PREFIX`): `q` detach, `space` fleet menu, `n`/`p` tab, `s`
next project, `h`/`j`/`k`/`l` or the arrows to move focus, `r` redraw, and
Ctrl+B again to send it through.

### Verified

Unit and daemon tests: 982, green on Windows, on Linux in Docker, and in CI on
both runners. The daemon tests run a real fleetd over a real pipe or socket
with fake PTYs. The switching acceptance tests pass there:
- a switch never spawns or kills;
- panes and processes survive hide/show round trips;
- two clients show different projects;
- a connection with no client cannot change a view;
- hidden agents keep receiving output.

Real consoles on Windows (conhost, driven by console input records), with
`FLEET_CONFIG_HOME` pointing at a scratch config:

| Check | Result |
|---|---|
| `fleet` picker → project opens as a workspace, fleetd started on demand | ✓ claude and the dashboard side by side, divider, status bar |
| Terminal.Gui dashboard rendered through the emulator | ✓ |
| Ctrl+B Space | ✓ fleet menu as an overlay over this client only |
| Menu → Switch project → a project not yet open | ✓ its workspace is created and shown. `switch demo1 -> demo2: 50–93 ms` including the two spawns |
| Ctrl+B S between open projects | ✓ fleetd frame 13–16 ms, 1.9–3.8 KB |
| Second client attached to `demo1` while the first switches four times | ✓ the second never changes |
| Ctrl+B Q, then attach again | ✓ clean exit, then a character-for-character identical screen |

Linux in Docker, under `script`, with a shell standing in for claude:

| Check | Result |
|---|---|
| Cold `build.sh`, `dotnet test`, AOT publish | ✓ 26.5 MB, needs only libc and libm |
| picker → open → menu overlay → open second project → switch → detach | ✓ `switch demo1 -> demo2: 6 ms`, frames 3–4 ms, fleetd still running after detach |
| `fleet attach --ssh localhost` against a fleetd on the "remote" | ✓ frames arrive over `ssh -T host fleet bridge`, typed input runs in the remote pane |

Switch frames are small because they are diffs against what the client
already shows. Two workspaces laid out alike differ in a few cells, so a
switch can cost under 100 bytes.

### Found by running it, and fixed

- **ConPTY children inherited redirected std handles.** fleetd was started
  with pipes for stdin, stdout and stderr, and a pane child (claude, cmd) took
  those pipes as its stdout instead of the pseudoconsole. It printed nowhere
  and died. The Terminal.Gui dashboard worked only because it opens `CONOUT$`
  by name. fleetd is no longer started redirected on Windows, and it releases
  any redirected std handles it finds at startup.
- **RoyalApps' `ProcessExited` does not fire.** Dead panes, and the menu
  overlay, stayed forever. Each Windows pane now also waits on its child
  process by PID and raises the exit once.
- **The socket directory chmod.** The listener made the socket's directory
  0700 even when it already existed, which fails for a non-root user on
  `/tmp`. Only CI caught it, because the Docker runs were root.
- **ssh prompts in raw mode.** An unknown host key or a password prompt
  cannot be answered once the terminal is raw, so `--ssh` hung. ssh then ran
  with `BatchMode=yes`, which refused password logins outright. Since
  2026-09-29 the client sends its hello and waits for fleetd's welcome before
  the terminal goes raw, so ssh asks for a password or a new host key in the
  ordinary terminal; ssh reads those from the terminal itself, not from the
  stdin fleet pipes into it.
- **Zig 0.16 on Windows, intermittently.** The first fetch of a dependency
  can fail with `file_hash FileNotFound` under `zig-pkg`; a second run
  succeeds. Two of three cold local builds hit it. `build.ps1` retries up to
  three times.

### Deviations from the plan

- **Linux clients send raw bytes.** DESIGN rule 3 asks for neutral key events.
  Windows clients send them (plus the raw record). Linux clients still send
  stdin bytes, and the prefix is a byte check, because there is no host input
  parser yet. A pane asking for the kitty keyboard gets legacy keys from a
  Linux client.
- **Layouts come only from fleet's own splits.** There is no interactive pane
  splitting or resizing by the user.
- **Size policy** is "most recently active client wins" for a workspace shown
  in two clients of different sizes. With two differently sized clients on
  one workspace, the panes resize whenever activity moves between them.

### Still open

- **Not exercised in a real console:**
  - hiding and unhiding a real agent (covered by feature and daemon tests only);
  - scrollback;
  - mouse, which is not forwarded at all yet;
  - clipboard, title and other host effects (rule 5);
  - bracketed paste into a ConPTY pane: the herdr pasted-Enter item.
- **Hosts I could not drive:** Windows Terminal, WezTerm, and Ghostty/kitty
  for the attach client. Also Linux→Windows over OpenSSH, which needs a
  Windows sshd.
- **Whether fleetd survives closing the terminal that started it.** On
  Windows it is a separate windowless process, but that is untested.
- **The conhost window grew after the picker.** In the automated runs the
  window reported 200x48 or 215x53 after `fleet`'s Terminal.Gui picker exited
  and the attach began. An attach started directly stayed at 120x30. The
  cause is unconfirmed; Terminal.Gui resizing the buffer on shutdown is a
  guess.
- **One unexplained client exit.** A second attached client left once,
  shortly after the first client's menu overlay ended. That was on the build
  before exit detection was fixed, and it did not reproduce afterwards.
- **The dashboard's other WezTerm-only actions** (e.g. its keybind hints) have
  not been audited for `embedded`.

## Paste on Windows, 2026-09-28

The gap: a multi-line paste into a Windows pane submitted each line. Pasting
two lines into Claude sent the first as a prompt.

### What the probes showed

Two probes ran inside fleetd panes: one reads console records (like
PowerShell's `ReadKey`), and one reads VT input (`ENABLE_VIRTUAL_TERMINAL_INPUT`,
as modern TUIs do). Both enable bracketed paste (`?2004h`).

| Path | What the program receives |
|---|---|
| `ESC[200~alpha CR bravo ESC[201~` into a pane, record reader, inbox ConPTY | `a l p h a`, an **Enter key press**, `b r a v o`. No markers |
| same, record reader, app-local ConPTY (WezTerm's OpenConsole 1.22) | the same |
| same, **VT-input** reader, inbox ConPTY | `ESC[200~alpha<CR>bravo ESC[201~` intact |
| same, VT-input reader, app-local 1.22 | intact |
| the app's `?2004h`, seen by fleetd through ConPTY | yes: `bracketed-paste=True` |

So a VT-input app, Claude included, gets a real bracketed paste as long as
something sends one. The markers must come from fleetd, and fleetd must know
the host pasted.

On the host side, where `fleet attach` reads its own console:

| Host | How a paste arrives |
|---|---|
| conhost, native Paste | one read of key records, CR as an ordinary key, **no markers** even though the client asked for `?2004h`. conhost also ignored `?9001h` |
| a terminal pasting through its ConPTY (WezTerm, Windows Terminal), reproduced with an outer fleetd | key records, **split into two reads at the newline**, markers stripped |

In record mode typed keys arrive one or two records per read, and a paste
arrives as a burst.

### Design

- **Client (Windows).** `PasteBurst` recognises a paste as a burst. A read
  with two or more text key-downs and no Ctrl/Alt chord (AltGr counts as text)
  starts a candidate; the client keeps reading while more input arrives within
  15 ms. The burst is a paste if it contains a newline or is at least 8
  characters long. Otherwise it is replayed as keys, so `dd` typed quickly in
  nvim still deletes a line. CRLF and LF become CR, as a terminal sends. A paste
  goes to fleetd as one `Text { paste: true }` message.
- **fleetd.** A paste is wrapped in `ESC[200~ … ESC[201~` when the pane asked
  for bracketed paste, and `ESC[201~` inside the text is removed so a paste
  cannot close its bracket early. A pane that did not ask gets the text as
  typed, Enters included, which is what a terminal does too.
- **Linux clients** are unchanged: their paste arrives as raw bytes, already
  bracketed if the host terminal does it.

Rejected along the way:
- **Reading the host as VT input** and asking it for `?9001h`/`?2004h`, as
  herdr does. conhost honours neither, and VT input loses key fidelity where
  `?9001h` is ignored.
- **Shipping an app-local ConPTY for paste.** The inbox ConPTY passes
  bracketed paste to VT-input apps just as well.

### fleet's own ConPTY replaces RoyalApps

To test the app-local ConPTY question, Windows panes moved to fleet's own
ConPTY code (`Pty/ConPtyPane.cs`). It stays, and the RoyalApps package is gone:
- it owns the process handle, so exit is seen from the handle rather than an
  event that did not fire;
- the child gets null std handles, so it can never inherit fleetd's;
- it builds the child's environment block itself.

It binds `CreatePseudoConsole`, `ResizePseudoConsole` and `ClosePseudoConsole`
from `kernel32` (the inbox ConPTY, the default) or from an app-local
`conpty.dll`. `FLEET_CONPTY` may name one, or say `inbox`. A `conpty.dll` with
its `OpenConsole.exe` next to `fleet.exe` is also picked up. Microsoft ships
that pair as the `Microsoft.Windows.Console.ConPTY` NuGet package (1.25
preview), should a newer ConPTY become worth shipping. `WindowsCommandLine`
builds the command line: C-runtime quoting, `.cmd`/`.bat` shims through `cmd`,
and anything after `cmd /c` passed as written.

### Verified

- Unit tests: `PasteBurst` (bursts, split reads, chords, AltGr, surrogates,
  newline normalisation), `PasteBytes` (bracketing only when asked; an embedded
  `ESC[201~` removed) and `WindowsCommandLine`. 1003 tests.
- conhost's native Paste into a real attach client → the VT app received one
  bracketed paste.
- A paste through a ConPTY into the attach client, which is the
  WezTerm/Windows Terminal path, run windowless with an outer fleetd → one
  bracketed paste.
- Claude (plan mode) in a pane: two pasted lines sat in its prompt box as one
  paste, and nothing was submitted.

### Limits

- **A single-line paste under 8 characters** reaches the pane as keys, not as
  a bracketed paste. That is deliberate, so short fast input in nvim stays
  keys.
- **A human typing two keys within one read** is treated as a burst. It still
  becomes a paste only with a newline or 8+ characters, which typing does not
  produce.
- **Programs that read console records** (PowerShell/PSReadLine) receive a
  paste as keys with Enters, since the inbox ConPTY strips the markers for
  them. That is the same as pasting into them in any terminal.
- **Not tried in a real WezTerm or Windows Terminal window.** Test windows take
  the keyboard focus, even when launched minimized and not activated, so these
  runs used conhost's Paste command and the windowless nested setup instead.
  Paste into Claude inside `fleet attach` under WezTerm belongs on the manual
  checklist.

## Mouse, 2026-09-28

tmux-style mouse support for `embedded`.

### Behaviour

- **Click** focuses the pane under the pointer, and the click also reaches that
  pane.
- **Drag** stays with the pane it started in. Coordinates are clamped to that
  pane, so a selection drag in nvim that leaves the pane keeps working.
- **Wheel** goes to the pane under the pointer, without moving focus.
- **Status bar:** clicking a tab label shows that tab.
- **Dividers:** dragging one resizes the split. Both panes are resized and
  their programs get SIGWINCH or a ConPTY resize.
- **Menu overlay:** it takes the clicks inside its box.
- A pane's program only receives mouse input if it asked for it (1000, 1002,
  1003). libghostty-vt's mouse encoder decides this from the pane's terminal
  state and encodes in the format the program chose (X10, UTF-8, SGR, urxvt).
  Coordinates are pane-relative.

### How it gets there

- **Windows client:** `ENABLE_MOUSE_INPUT` on the attach client's console, and
  `WindowsMouse` turns `MOUSE_EVENT` records into press, release, motion and
  wheel. Motion is sent only when the cell changes, and coordinates are made
  relative to the visible window. This also covers WezTerm and Windows
  Terminal: their ConPTY turns the terminal's SGR reports into those records.
- **Unix client:** asks the host for `?1002h?1006h` and takes the SGR reports
  out of stdin (`SgrMouse`). Everything else passes on untouched, and a lone
  Esc is never held back.
- **Wire:** a `Mouse` message (x, y, button, action, mods, whether a button is
  held).
- **fleetd:** `MuxModel.Hit` for panes, the overlay, dividers and the status
  bar; `Composer.TabSpans` for tab labels, shared with the renderer so they
  cannot disagree. A per-client capture keeps a drag with its target.
- **Opting out:** `FLEET_MOUSE=off` leaves the mouse to the host terminal. With
  mouse capture on, native text selection needs Shift in WezTerm, Windows
  Terminal, Ghostty and kitty, and is off in conhost.

### Verified

- **Tests:** 17 new, for record translation, SGR parsing (split reads, a lone
  Esc, other sequences untouched, hover vs drag), hit-testing, divider drags
  and clamping. fleetd's routing is tested end to end with fake panes: click to
  focus, a drag that leaves its pane, wheel without focus, and tab clicks.
- **Windows, windowless:** SGR reports sent through a ConPTY into
  `fleet attach`, the WezTerm/Windows Terminal path, reached an SGR app
  exactly: press, release, wheel, and column 110.
- **Windows, nvim:** five wheel-downs scrolled a 200-line file to `line 16`.
- **Linux in Docker:** SGR wheel reports into `fleet attach` scrolled nvim to
  `line 16`, read back from fleetd.

### Not verified

- **conhost's own mouse records from a physical mouse.** Test windows take
  focus, so none were opened; the record format is the same one the ConPTY
  path produces.
- **Divider dragging and tab clicks in a real terminal.** They are covered by
  the model and daemon tests.
- **Clicking in a pane focuses and passes the click on in one go.** A program
  that treats a click as an action (e.g. a TUI button) acts on the focusing
  click too. tmux behaves the same with `mouse on`.

## Window title and clipboard, 2026-09-28

Rule 5 of *Remote attach*, implemented: a pane's title and its clipboard writes
act on the machine the user is sitting at, as messages rather than escape
sequences passed through.

### Behaviour

- **Window title.** Each client's terminal title is the focused pane's title
  (OSC 0/2) and the workspace, e.g. `README.md - NVIM · techweb`, or
  `techweb · fleet` when the pane set none. It is sent only when it changes,
  and it follows focus, switching and overlays. The original title comes back
  on detach: `GetConsoleTitle`/`SetConsoleTitle` on Windows, the xterm title
  stack (`CSI 22 t` / `CSI 23 t`) on Unix.
- **The emulator's title is also the model's pane title.** fleet's own
  features read it, e.g. `SubBrowse.Is` recognises a sub-orchestrator's
  browser by a title ending in ` files`, so on `embedded` it now comes from
  the program itself.
- **Clipboard writes.** OSC 52, and the iTerm2 and kitty variants that
  libghostty-vt normalises, deliver plain text up to 1 MiB to one client: the
  most recently active client showing that pane, or else the most recently
  active client. That client sets the clipboard:
  - natively on Windows (`SetClipboardData`), which works in conhost, Windows
    Terminal and WezTerm alike;
  - as OSC 52 to its host terminal on Unix, which also works when the client
    is on the far side of SSH.
- **Clipboard reads** (OSC 52 `?`) stay refused. A program in a pane never
  reads the user's clipboard.
- **Titles are sanitised** (control characters removed, 256 characters
  maximum), so a pane cannot put escape sequences onto the host through its
  title.

### Verified

- **Tests:** fleetd with fake panes. The title follows the pane and the
  workspace, including after a switch. A copy reaches only the client showing
  that pane. The emulator's title shows up in `list-panes`. Sanitising and the
  OSC 52 encoding have their own tests (1026 tests).
- **Windows, windowless, real emulator:** a probe pane's OSC 52 landed on the
  Windows clipboard. The attach client's own title, as its outer fleetd saw
  it, became `effects probe title · probe`.
- **Linux in Docker:** `fleet attach` wrote the title push, the OSC 0 title,
  the OSC 52 copy with the right base64, and the title pop.

### Not verified

- **Whether each host terminal honours OSC 52 writes.** Ghostty, kitty,
  WezTerm and Windows Terminal do by default; others may need a setting. On
  Windows this does not matter, because the client sets the clipboard
  natively.

## Floating panes, 2026-09-28

zellij-style floating panes on `embedded`. A float is an ordinary pane of a
workspace, drawn in a bordered box over the tiled layout instead of in it.

### Behaviour

- **Per workspace.** Each workspace has its own floats, in z-order, and one
  shown/hidden switch. Every client showing that workspace sees the same floats.
- **Prefix keys** (`ctrl+b` by default):
  - `f` opens a float with the default shell, in the focused pane's directory;
  - `w` shows or hides the workspace's floats;
  - `e` tiles the focused float beside the active pane, or floats the focused
    tile;
  - `g` enters float mode for the focused float: `h/j/k/l` or the arrows move
    it one cell, `H/J/K/L` or shift+arrows make it narrower, taller, shorter
    or wider by one cell, and `esc`, `enter`, `q` or `g` leave. The status bar
    shows the keys while the mode is on. Other keys are swallowed, so nothing
    typed in the mode reaches a pane.
- **Focus.** A new or clicked float comes to the top and takes the keys. A
  click on a tile, or `w` to hide, gives the keys back to the tiles. `h/j/k/l`
  move between tiles only.
- **Hidden floats keep running**, like hidden workspaces. The status bar shows
  ` float N ` whenever a workspace has floats (highlighted while they are shown),
  and clicking it toggles them.
- **Mouse.** Dragging the border moves a float. Dragging the bottom-right corner
  resizes it (at least 10x4). A float never leaves the screen: it is clamped to
  the client's size at draw time, and its pane is sized to the inside of the
  box. Clicks and wheel inside it reach the program in its own coordinates, as
  they do for tiles.
- **Default size and placement.** 60% of the width and height, centred, with
  each further float offset so none hides the one before it exactly.
- **Titles.** The border shows the float's title: the one set through
  `title`, or else the program's own (OSC 0/2).
- **Lifetime.** Floating the last tile keeps the workspace alive. A workspace
  goes away only when its last tile and its last float are gone. A float cannot
  be split.
- **Control.** `spawn-float` opens one from outside (fleetctl, scripts);
  `list-panes` reports floats with tab `float`.

### Verified

- **Tests** (33 new, 1059 in all):
  - model: placement, hit-testing (border, corner, inside, topmost wins),
    raise and lower on focus, clamping on move and resize, float to tile and
    back, workspace lifetime, per-workspace floats;
  - compositor: box, title and cursor;
  - fleetd with fake panes: `f` then keys reach the float and `w` hands them
    back; dragging the border moves the float without the pane seeing the
    drag; `e` tiles and floats again; `float-move`/`float-size` nudge the
    focused float;
  - float mode: keys and shifted keys, and the Unix byte path including
    arrow and shift+arrow sequences.
- **Windows, windowless, real binary and emulator:** an outer fleetd ran
  `fleet attach` in a pane against an inner fleetd, and the chords and SGR
  mouse went into that pane the way a terminal sends them through ConPTY.
  - `ctrl+b f` drew the box at column 24, row 8, and `echo FLOAT-OK` ran
    inside it.
  - A drag of the top border from (30,8) to (20,4) moved it to (14,4).
  - `ctrl+b w` hid it; the pane survived.
  - `ctrl+b w`, `ctrl+b e` tiled it next to the existing pane (both in `t1`).
  - Float mode, in a second run: `ctrl+b g`, `lll`, `j` moved the box from
    (24,8) to (27,9); `LL` widened it from 72 to 74 columns. `esc` left the
    mode, and `echo AFTER-ESC` then ran in the float. The float's own text had
    none of the mode's keys in it.

### Not done

- ~~**No port operation.**~~ Added with the approval float:
  `IMuxDriver.SpawnFloatingAsync`, behind `MuxCaps.Popup`. See "The menu and
  approvals as floats".
- **Float mode on Unix was not run end to end.** Its byte path (letters,
  arrows, shift+arrows) is unit-tested; the Windows key path was run for real.
- **Float layout is not saved** across fleetd restarts. Neither is anything
  else yet.

## Windows input checklist, 2026-09-28

The items from herdr's CHANGELOG (see "Windows input checklist for later
phases"), checked against `embedded`.

### How keys reach a pane

- **A ConPTY pane** (every pane on Windows) asks for win32-input-mode
  (`?9001h`). fleet then hands it the client's own key records, as Windows
  Terminal does. ConPTY rebuilds exactly those records for the program, and
  conhost does any VT translation as it would under Windows Terminal.
- **Any other pane** (a Linux pane reached from a Windows client) gets the
  key encoded by libghostty-vt from fleet's translation of the record.
- **A Unix client** passes the bytes it reads straight through. It only takes
  out the prefix, SGR mouse reports and float-mode keys.

### Found and fixed

- **Key repeat was multiplied.** A record with repeat count 3 reached the
  pane as the record *plus* two extra encoded keys: five `x` instead of three.
  The key message now carries a `repeat` count. fleetd passes the one record
  through to a ConPTY pane, and encodes N presses for any other pane.
- **Dead keys** have no text. On the encoder path, the dead key's press could
  have become a stray base character. fleet now marks a dead key (Windows
  flags it in `MapVirtualKey`) and encodes nothing for it. The composed
  character arrives once, with the next key.
- **`y` + Enter typed in one console read counted as a paste**, so the pane
  got it bracketed and Claude Code would not submit. A burst under 8
  characters is now a paste only when a newline sits between text.
- **The host was not restored when the client was killed.** SIGTERM, SIGHUP
  (also console close on Windows), SIGQUIT and process exit now restore it,
  exactly once. The restore resets mouse (1000/1002/1003/1006), focus (1004),
  bracketed paste (2004), cursor keys, keypad, cursor shape and visibility,
  synchronized output, attributes and the alt screen. It also restores
  console modes, code pages, the console title and termios.

### Verified

- **End to end on Windows, real binary, windowless.** An outer fleetd ran
  `fleet attach` in a pane. Each key was injected as win32-input-mode input,
  which makes the outer ConPTY produce exactly those records for the client.
  In the inner pane, a probe printed what it received.
  - **Record mode:** the program got every injected record unchanged, with
    virtual key, character, key-down, control state and repeat count all
    matching. That covered Shift+Tab, Shift+Enter, Esc, Alt+a, Alt+Shift+a,
    Alt+Backspace, Ctrl+Alt+a, Ctrl+S, Ctrl+J, Ctrl+/, Ctrl+1, AltGr+q (`@`),
    Right Alt+a, the dead key `'` then `é`, a surrogate-pair emoji, and `x`
    with repeat 3.
  - **VT-input mode, as conhost translates it:**

    | Key | Program got |
    |---|---|
    | Shift+Tab | `\e[Z` |
    | Shift+Enter | `\r` |
    | Esc | `\e` |
    | Alt+a | `\ea` |
    | Alt+Shift+a | `\eA` |
    | Alt+Backspace | `\e\x7f` |
    | Ctrl+S | `^S` |
    | Ctrl+J | `^J` |
    | Ctrl+/ | `^_` |
    | AltGr+q | `@` |
    | `'` then `e` | `é` (UTF-8, once) |
- **Tests (15 new, 1074 in all):**
  - key translation: modifiers, AltGr, Ctrl letters, dead keys, surrogate
    pairs, repeat counts;
  - chords never taken for a paste; the short-burst rule;
  - the restore sequence;
  - a split OSC reply passing through the Unix byte path whole;
  - in fleetd: repeat encoding for both kinds of pane, and a dead key putting
    nothing into an encoder pane.

### Limits

- **Emoji for VT-input programs.** A program that reads console input with
  `ReadFile` in VT mode gets U+FFFD for each half of a surrogate pair. This
  happens even when the emoji arrives as plain UTF-8 text, so it is the inbox
  conhost, not fleet. Programs that read records (node/libuv, PSReadLine) get
  the right character. A newer `conpty.dll` via `FLEET_CONPTY` may fix it;
  not tried.
- **What conhost makes of a record is conhost's.** Shift+Enter reaches a
  VT-input program as a plain `\r`, the same as under Windows Terminal.
- **The host can still claim a key before fleet sees it,** e.g. WezTerm's
  leader on Ctrl+S. That is host configuration.
- **The encoder path (Windows client, Linux pane) was checked with unit tests
  only,** not end to end.
## The menu and approvals as floats, 2026-09-28

### Behaviour

- **The menu is a float in the workspace you are in.** `ctrl+b space` opens
  `fleet menu` as a modal float (80% of the screen, titled `fleet menu`) in
  the workspace the client is showing, instead of in a per-client overlay
  workspace. A second press brings the open menu forward rather than opening
  another. When the menu exits, the keys go back to the pane you were in.
- **Its actions happen where you are.** The menu's own pane now belongs to
  the project's workspace, and `Adapters.CurrentWindow` asks the driver for
  its own pane (before, it read `WEZTERM_PANE` only). So *Edit fleet config*
  and *Browse files* open in that workspace. On `embedded` they used to take
  the first active pane anywhere, which could be another project.
- **Its tools open as floats too** (added later the same day). The file
  navigator (menu `f`, and the dashboard's browse), *Edit fleet config*
  (menu `S` `E`) and the folder picker use `Adapters.SpawnHereAsync`:
  - when the multiplexer has floats and fleet runs inside one of its panes,
    they open as an ordinary float (60%, movable and resizable, closing with
    the program) in the caller's workspace;
  - otherwise they open a pane as before (WezTerm, or a plain terminal).

  `SpawnFloatingAsync` with no `over` pane means exactly this.
  - Verified with the real binary: menu `f` opened yazi as a float titled
    `files` in the project's workspace, and `q` closed it and the float.
    Menu `S` `E` opened a `fleet config` float there.
  - Tests: the helper in all three cases, and `spawn-float` from a pane in
    fleetd.
- **The overlay is only a fallback,** for a client that shows no workspace.
- **Modal floats** (the menu, approvals):
  - are drawn even while the workspace's floats are hidden, and do not reveal
    them;
  - are not counted in ` float N ` and not affected by `ctrl+b w`;
  - cannot be tiled with `ctrl+b e`.
- **Every client showing the workspace sees the menu,** because floats belong
  to the workspace. The overlay was per client.
- **An agent's approval request opens over the agent's pane.** The agent's
  fleet MCP server runs inside that pane and knows its id.
  1. On a multiplexer with floats (`MuxCaps.Popup`), the MCP server tags the
     request with its pane.
  2. It opens `fleet approve --project <p> <pane>` in a modal float sized
     from that pane (50–72 columns by 10–14 rows) and centred in the whole
     window, so it does not land in whichever split asked (changed
     2026-10-04; it used to be centred over the pane).
  3. That float shows the same Allow/No dialog as the dashboard, answers,
     and exits. The MCP side closes the float if it is still there when the
     answer arrives by some other route.
- **The dashboard stays the fallback.** It takes a pane-tagged request only
  after 5 seconds, i.e. when no float picked it up (it failed to start, or
  the agent's workspace is not shown anywhere). Untagged requests reach it at
  once, as before. On WezTerm nothing changes: no floats, so no tag.
- **Port:** `IMuxDriver.SpawnFloatingAsync(over, options)`. `embedded`
  implements it (control op `spawn-float` with `pane`). WezTerm refuses it,
  the fake supports it when it has workspaces, and `FailSilentDriver` guards
  it.

### Verified

- **Tests (10 new, 1084 in all):**
  - menu float in the shown workspace, single instance, keys back afterwards;
  - overlay fallback;
  - modal floats over hidden floats, not toggled, not counted, not tiled;
  - float-over-pane placement;
  - `spawn-float` over a pane in fleetd;
  - approval requests taken only by their pane's float, then the dashboard's
    grace;
  - the MCP-side decorator: opens the float over the asking pane, closes it
    after the answer, and does nothing without a pane or without floats.
- **Windows, windowless, real binary** (outer fleetd running `fleet attach`
  against an inner fleetd):
  - `ctrl+b space` drew `╭─ fleet menu ─…` over the agent pane, listed as
    tab `float` in workspace `probe`. `esc` closed it, and
    `echo BACK-IN-PANE` then ran in the pane underneath.
  - A pane-tagged request written the way the MCP side writes it, plus
    `spawn-float` over the agent pane running `fleet approve`, drew the dialog
    centred over the agent (column 24, row 12 on 120x39). Enter wrote
    `Allowed` to the reply file, and the float closed.

### Not verified / limits

- ~~**The full MCP round trip**~~ was run later the same day with a real
  agent (windowless, isolated config). The setup:
  - an inner fleetd ran the real `fleet dash` and Claude Code (`claude -p`)
    with this build's `fleet mcp` as its MCP server;
  - an outer fleetd ran `fleet attach`, so the float was drawn in a real
    client;
  - Claude was asked to call `stop_agent` (Ask by default) for a
    non-existent agent.

  What happened:
  - The float `approve?` opened over the Claude pane 8–10 s after the prompt
    started, showing "the main orchestrator wants to: Stop an agent —
    nobody/none".
  - **Allow** (Enter): the tool ran, and Claude reported its real result, "No
    agent nobody/none in this project".
  - **No** (Esc): Claude reported that the stop was declined in fleet.
  - Both times the float closed, and the fleet log recorded the request and
    the outcome.
- **An approval float takes the keyboard in that workspace** when it opens,
  as a dialog does. If the agent's workspace is not on any screen, the float
  waits there until you switch to it, or the dashboard takes the request
  after 5 seconds.
- ~~**Underscores in the tool name are eaten.**~~ Fixed: dialog lines
  (`FleetTheme.Caption`) no longer treat `_` as a hotkey marker. A decline now
  reads "Declined by the user in fleet." instead of naming the dashboard.
## Scrollback and copy mode, 2026-09-28

### Behaviour

- **History lives in the emulator.** libghostty-vt keeps each pane's
  scrollback (its default limits) and can scroll its own viewport, so fleetd
  stores nothing extra. Rendering follows the viewport.
- **The mouse wheel over a pane:**
  - goes to the program if it turned on mouse reporting (vim, less `--mouse`,
    TUIs), as before;
  - on the alternate screen without mouse reporting (less, man), becomes three
    arrow keys, like "alternate scroll" in other terminals;
  - otherwise scrolls the pane's history three lines at a time.
- **A scrolled-back pane shows where it is.** A marker `[below/history]` sits
  at its top-right corner (lines below the view / lines of history).
- **Typing snaps back.** Any key or text sent to a scrolled-back pane first
  returns it to the live output.
- **Copy mode** (`ctrl+b [`, tmux's key). It works on the focused pane
  (tiled or floating), and the status bar shows its keys while it is on.

  | Key | Does |
  |---|---|
  | `h/j/k/l`, arrows | move |
  | `0` / `^` / Home, `$` / End | line start / end |
  | `g`, `G` | top / bottom of history |
  | `ctrl+u` / `ctrl+d` | half page |
  | PgUp / PgDn | full page |
  | `v` or space | start/stop a selection |
  | `y` or Enter | copy and leave |
  | `q`, Esc, `ctrl+c` | leave |

  - The view scrolls to keep the cursor visible, and the selection is shown
    inverted.
  - `y` without a selection copies the cursor's line.
  - The copy goes to the client's clipboard by the existing path (natively on
    Windows, OSC 52 on Unix). Leaving returns the pane to live output.
  - Other keys are swallowed, so nothing typed in copy mode reaches the pane.
- **How it is split:**
  - The client maps keys to `copy <step>` commands, from Windows key records
    or Unix bytes (including the arrow, Home/End and PgUp/PgDn sequences),
    through the same sticky-mode path as float mode.
  - fleetd keeps the copy state per client (`CopySession`): cursor and
    anchor in absolute history rows.
  - To extract the text, fleetd pages the viewport through the selected rows
    and restores it afterwards.

### Verified

- **Tests (24 new, 1108 in all):**
  - copy-mode movement, the view following the cursor, clamping, selections
    across lines, yank without a selection, and leaving back to the live
    output;
  - the scrolled-back marker, and the inverted selection with the copy
    cursor;
  - key mapping for Unix bytes and Windows keys;
  - the `[` chord;
  - in fleetd: the wheel scrolling history and typing snapping back, the
    wheel becoming arrows on a full-screen program, and a copy-mode yank
    reaching the clipboard of the client that copied.
- **Windows, windowless, real binary and libghostty** (outer fleetd running
  `fleet attach` against an inner fleetd whose pane printed `LINE-1` to
  `LINE-200`):
  - five wheel-ups (SGR reports, which ConPTY hands the client as wheel
    events) showed `LINE-151` at the top with `[15/165]`. One wheel-down gave
    `[12/165]`, and typing `x` snapped back.
  - `ctrl+b [` showed the copy-mode keys in the status bar, and `g` jumped to
    `LINE-1` with `[165/165]`.
  - `0 v j $ y` put `LINE-1\nLINE-2` on the Windows clipboard and returned the
    pane to live output.

### Limits

- **One viewport per pane.** Two clients showing the same pane share its
  scroll position.
- **Soft-wrapped lines are copied as separate lines.** The copy joins screen
  rows with `\n`, and the copied line breaks are `\n`, not `\r\n`.
- **No mouse selection** (drag to select in panes that do not use the mouse)
  and **no search** (`/`) in copy mode yet.
- **Scrolling with the wheel while in copy mode** can move the copy cursor out
  of view. It is then hidden until a key brings the view back to it.
- **Unix clients were covered by unit tests only,** not end to end.
## Running the embedded build, 2026-09-28

`scripts\embedded.ps1` runs this checkout's fleet with `FLEET_MUX=embedded`,
next to an installed fleet and without touching it. It builds on first use:
libghostty-vt if it is missing, then `dotnet publish`, with the Visual
Studio installer folder put on `PATH` for NativeAOT's `vswhere`.

| Command | Does |
|---|---|
| `scripts\embedded.ps1` | pick a project; fleetd starts and this terminal attaches |
| `scripts\embedded.ps1 -Project <p>` | attach straight to a project |
| `scripts\embedded.ps1 attach` | reattach after a detach |
| `scripts\embedded.ps1 build` | build a new copy next to the running one; fleetd keeps running |
| `scripts\embedded.ps1 restart` | stop fleetd and attach with the newest build |
| `scripts\embedded.ps1 stop` | stop fleetd and its clients (matched by path, so an installed fleet is left alone) |
| `scripts\embedded.ps1 status` | the newest build against the last code commit, and which build each running fleet uses |

`-Isolated` uses a separate config under `artifacts\embedded-config`, so no
real projects are touched. The script restores `FLEET_MUX` and
`FLEET_CONFIG_HOME` in the calling session when it returns.

**Side-by-side builds (2026-09-29).** Windows locks a running exe, so the
first version stopped fleetd before every build, which closed the session and
every agent pane in it. Each build now goes into its own folder,
`artifacts\embedded\<rid>\<yyyyMMdd-HHmmss>`, and a `current` file names the
newest. `attach` and `run` start the newest; a running fleetd keeps the build
it started from until `restart`. Builds that no process runs are pruned,
keeping the three newest. The old `artifacts\publish\<rid>\fleet.exe` is still
used (and recognised as running) until the first new-style build.

**End-to-end scripts (2026-09-29).** They are run by hand before a merge, not
in CI: they start real processes and take about a minute.

| Script | Covers |
|---|---|
| `scripts\e2e\windows.ps1 [-Fleet <exe>]` | its own fleetd and a scripted attach client recording frames: ConPTY panes, the dashboard in a pane, the warm menu (shown under 200 ms, in one frame), Settings in one frame, floats, restore after a kill. The Windows console client itself is not covered |
| `scripts/e2e/linux.sh <fleet>` | tmux as the real terminal around `fleet` and `fleet attach`: the Unix client and PTYs, which-key, menu, splits, tabs, zoom, floats, a resize, the mouse, copy mode, switching project, detach and reattach, restore after `kill -9` |

Both isolate fleetd (own config, own pipe or socket) and stop only the fleetd
they started, so a session running from the same build is left alone. They
exit with the number of failed checks. From Windows, run the Linux script on
the binary CI built: `gh run download <run id> -n fleet-linux-x64 -D
artifacts\e2e\linux`, then `wsl -e bash scripts/e2e/linux.sh
artifacts/e2e/linux/fleet`.
## Keys, which-key, the tab bar and dashboard menus, 2026-09-29

After merging `main` (orchestrators' Claude inside nvim, `update <version>`,
`CLAUDE.md`), the embedded client took over the WezTerm tmux-mode layout.

### Tab bar

The status row is at the **top** now, drawn as Catppuccin pills like the
tmux-mode bar:
- the project;
- the active tab as a pill, the other tabs as dim text, with ` Z` on a
  zoomed tab;
- the float count;
- the badge on the right.

Panes start on row 1, and `MuxModel.Content` is the one place that defines
the content area. Clicks on tabs and the float count use the same segment
list as the drawing.

### Keys (`<fleet config>\embedded-keys.json`)

The defaults started as a copy of `~/.wezterm/tmux-mode.lua`; since the
which-key submenus (2026-10-06) the rarely used keys sit in groups, so they no
longer mirror it one to one. The file only needs overrides; `"none"` unbinds a
key, comments and trailing commas are allowed, and `FLEET_PREFIX` still wins
for the prefix. `prefix q r` reloads the file.

```jsonc
{
  "prefix": "ctrl+s",
  "prefixKeys": {
    "v": "split-right",
    "x": "none",
    "g s": "split-down"     // a sequence: g opens a group, s runs in it
  },
  "groups": {
    "g": "git-ish stuff",   // the group's label; without one, the label is the key
    "q": "none"             // drops a default group and its children
  },
  "icons": {
    "g": "",          // a group's icon, keyed by the group's key
    "w": "none"             // removes an icon
  },
  "showIcons": true,        // false turns every icon off (a terminal without a Nerd Font)
  "keys": { "ctrl+h": "none" }
}
```

| Prefix key (default `ctrl+s`) | Does |
|---|---|
| `h j k l` | move focus |
| arrows | resize the focused pane by 5 cells |
| `%`, `"` | split right / down (a shell in the pane's folder) |
| `c`, `n`, `p`, `1`–`9` | new tab, next, previous, go to tab |
| `z` | zoom the focused pane (toggle; moving focus unzooms) |
| `x`, `&` | close pane / tab, after a `y/n` |
| `o` | next pane |
| `s` | switch project (the picker, as a float) |
| `space` | menu |
| `[`, `]` | copy mode / paste the Windows clipboard |
| `d` | detach |
| `f` › `f t e g` | **+float**: new float / show-hide floats / float↔tile / move-resize float |
| `w` › `w s` | **+project**: next project / switch project |
| `q` › `d q r` | **+session**: detach / detach / reload keys |
| the prefix again | sends the prefix to the pane (inside a group: back to the root) |

**Sequences and groups.** A `prefixKeys` spec with spaces (`"f t"`) is a
sequence; every key but the last opens a group. The popup stays open inside a
group and shows only that level, titled with the path (`ctrl+s › float`).
`esc` closes it, `backspace` goes up a level (`esc close · bksp back` in the
footer), and an unknown key closes it and does nothing. There is no timeout.
Depth is unlimited; the defaults use one level. Only prefix keys take
sequences: one under `keys` is dropped with a log line.

A key cannot be a leaf and a group on the same level. When they collide:
- the user's entry beats the default: binding `"f": "float-new"` makes `f` a
  leaf again and drops the default `f …` children (that is how to get the old
  flat layout back), and binding `"z x": …` drops the default `z` leaf;
- when both come from the user, the leaf wins and the log says
  `keys: "t x" ignored, "t" is already bound`.

A group whose children are all unbound disappears.

**Icons.** As in nvim which-key, submenu and fold rows get a Nerd Font icon in
front of the label, in the row's label colour; plain keys get none. The
defaults are float `` (window-restore), project `` (folder), session
`` (power-off), focus `` (arrows), resize `` (expand) and go to
tab `` (columns). The status bar's powerline caps already assume a Nerd
Font. Icons are BMP private-use code points only (U+E000–U+F8FF), because the
composer writes one `char` per cell and a surrogate pair would split; an
`icons` entry that is not a single basic-plane character is dropped with a log
line. `icons` covers default and user groups; the folds keep their built-in
icons, and `"showIcons": false` turns off all of them. On the wire the icon is
`WhichKeyEntry.icon`, left out when null, so an older daemon ignores it.

**The wire.** `WhichKeyEntry.Group` marks a real submenu (drawn `+label` in
blue); the display folds (`h j k l ➜ focus`, arrows, `1-9`) set the separate
`fold` flag and draw as plain rows. Both flags are optional on the wire, so an
older daemon draws folds as plain rows and a newer one reads an older client's
folds as plain rows too.

| Key without the prefix | Does |
|---|---|
| `ctrl`/`alt` + `h j k l` | move focus; when the pane runs nvim the key goes to nvim (the `is_nvim` rule) |
| `alt+←/→`, `ctrl+tab`, `ctrl+shift+tab` | previous / next tab |
| `ctrl+enter` | the menu: the dashboard's over a dashboard, fleet's elsewhere |
| `alt+o`, `alt+shift+o` | show or hide the head orchestrator's float (from the fleet keymap; see "The head orchestrator") |
| `shift+enter` | Claude's newline: Ctrl+J for a shell or Claude, Shift+Enter (CSI-u off Windows) for nvim |

- **Which-key.** Pressing the prefix draws a box at the bottom listing the
  prefix keys, with directions, arrows and tab numbers grouped.
- **Nvim at the edge of its splits.** main's orchestrator (Claude inside nvim)
  runs `$WEZTERM_EXECUTABLE cli activate-pane-direction <dir>` there. fleetd
  sets `WEZTERM_EXECUTABLE` to fleet itself, and `fleet cli
  activate-pane-direction` moves focus from the calling pane, so Ctrl+h/j/k/l
  crosses from nvim into fleet's panes.
- **Unix clients** get the same bindings from byte sequences (ctrl+letter,
  alt+x, arrows with modifiers, PgUp and friends). A direct binding only fires
  when it is the whole read, so a pasted newline is never taken for Ctrl+J.
  Ctrl+Enter, Shift+Enter and Ctrl+Tab have no distinct legacy encoding, so
  they work from Windows clients only.

### One fleet menu, built as the float (revised 2026-09-29)

- **One menu everywhere.** Ctrl+Enter, `prefix space` and the dashboard's own
  menu button all open the fleet menu (Quit, Go to dashboard, Switch project,
  List agents, File navigator, Settings) as a modal float. The dashboard
  button asks fleetd for it through the `menu` control op. The earlier
  per-dashboard menu is gone; its actions keep their direct keys in the
  dashboard.
- **The float is the frame.** fleetd gives floats `FLEET_FLOAT=1`. With it
  set, `FleetTheme.Screen`/`Overlay`/`Modal` build borderless windows that
  fill the float, so there is no second frame inside the float's border.
- **The float follows the screen inside it.** Each window pushes a
  `FloatScreen` (title and size) while it runs, and pops it when it closes.
  - The title becomes the console title, which reaches the float's border.
    For fleet's own floats (`fleet menu`, `fleet approve`), the live title
    wins over the name the float was given.
  - The size goes to fleetd's `fit` op:
    - the menu and pickers ask for their content size, and dialogs for their
      dialog size;
    - full screens (log, keybinds, settings) take the large area in the
      middle;
    - a sized float is centred in the whole window (until 2026-10-04 it
      kept its own centre, which left an approval in the asking split);
      one the user moved (drag or keys) keeps its own centre instead;
    - when a dialog closes, the screen below gets its title and size back.
- **Fixed on the way.** `FleetActionIds.Parse` did not know ids that fall back
  to the enum name (`viewlogs`, `browsefiles`, …), so such requests were
  silently dropped. It is now the inverse of `For` for every action.
### Verified

- **Tests:** 1158, format-clean.
  - key chords and their Unix bytes;
  - defaults, overrides, unbinding, a broken file, comments;
  - which-key grouping, the prefix state machine, the close confirmation;
  - model: resize, zoom, next pane, tab by number, focus from a pane, nvim
    and dashboard detection;
  - fleetd: split, smart focus with and without nvim, newline, close pane,
    the which-key box, `focus-from`, the dashboard menu float both ways;
  - every action id round-trips.
- **Windows, windowless, real binary** (outer fleetd running `fleet attach`
  against an inner fleetd; key records injected at the client):
  - Ctrl+S drew the which-key box, and `%` split right.
  - `z` zoomed and showed `1:shell Z`; a second `z` unzoomed.
  - Ctrl+H moved to the left shell, and typing reached it.
  - Ctrl+Enter opened the fleet menu float, and Esc closed it.
  - Ctrl+L then `x` `y` closed the right pane.
  - With a real `fleet dash`, Ctrl+Enter from the Claude-side pane and from
    the dashboard opened the same fleet menu. It was a 32-column float titled
    `fleet menu`, with no frame inside it.
  - *Switch project* with one project showed its dialog as a small float
    titled `Switch project`.
  - *Settings → Show log* grew the float to the large area, titled `fleet log
    — probe`.
  - Files and fleet config still open as floats.
  - With a real Claude agent, the approval dialog was a compact float over its
    pane, titled `Approve this action?`, and Allow reached Claude.

### Limits

- **Inside WezTerm with tmux-mode,** WezTerm takes Ctrl+S (its leader),
  Ctrl/Alt+h/j/k/l and Alt+arrows before `fleet attach` sees them.
  - Ctrl+Enter still reaches fleet: `fleet.lua` forwards it when no dashboard
    user var is present.
  - To pass the navigation keys through, add `or base == "fleet"` to
    `is_nvim` in `~/.wezterm/tmux-mode.lua`.
  - The leader cannot be made conditional in WezTerm. Either press Ctrl+S
    twice (WezTerm then sends a literal Ctrl+S), or give fleet another prefix
    in `embedded-keys.json`.
  - In Windows Terminal all keys reach fleet.
- **Not ported from tmux-mode:** rotate panes (`o` moves to the next pane
  instead), rename tab/workspace (`,`/`$`), and the tab navigator (`w` goes to
  the next project).
- **Keybinds edited from the dashboard menu float** are picked up by the
  dashboard the next time it starts, not at once.
- **Not run end to end:** the dashboard menu button's click path (fleetd
  tests cover the control op it uses), and the Unix byte bindings.
- **A float that becomes a tile** (`prefix e`) keeps `FLEET_FLOAT`, so a fleet
  screen in it stays borderless.
## Menu opening, hidden agents and repeated keys, 2026-09-29

- **Menu size.** The fleet menu and pickers have more room around their rows
  (menu: row width + 20 columns, at least 52, and 6 rows of padding).
- **No flash on opening.** A new menu float is created hidden; keys still
  reach it.
  - It is shown once it has fitted and its pane has drawn at that size. After
    a resize, it waits for content that differs from the cropped first
    drawing.
  - fleetd remembers the menu's size, so later menus start at it and need no
    resize at all.
  - Caps: 1.2 s after the fit, 2.5 s in all.
  - Measured with the real binary: the first open after fleetd starts goes
    from nothing to the full menu in about 0.7 s (cold start); later opens
    take about 0.1 s. Nothing large or empty shows in between.
- **Hidden agents stay hidden on the dashboard.** The dashboard's refresh
  took an agent as shown unless its panes sat in the global hidden workspace.
  `embedded` hides into `<project>~hidden`, so the hidden icon vanished on the
  next refresh. `AgentPanes.Shown` now treats any hidden workspace as hidden.
- **Repeated keys in Terminal.Gui programs.**
  - The problem: libghostty answered the kitty keyboard query that ConPTY
    passes through. Terminal.Gui then expects each key twice (kitty event and
    plain character) and swallows the next identical plain key. So `x` to
    show an agent right after hiding it, `j j`, and double letters were lost
    in the dashboard and fleet's menus.
  - Why it only happened here: the inbox ConPTY never delivers kitty keys.
    WezTerm with kitty off never answers the query.
  - The fix: fleetd drops the kitty flags report from a ConPTY pane's replies.
    Panes elsewhere keep it, because fleet's encoder can send them kitty keys.
  - Verified end to end: `z z`, `z z z` and hide → show all reach the real
    dashboard.
## Attach picker and clickable dashboard tabs, 2026-09-29

- **`fleet attach` without `--project`** asks fleetd for its project
  workspaces (hidden ones excluded) and their pane counts.
  - With more than one, it shows an `Attach to` picker (letter keys,
    `name … N panes`) before attaching. Esc attaches to nothing.
  - With one or none, it attaches as before.
  - `--ssh` attaches are unchanged.
- **Dashboard tabs** (Agents, Subs, Repositories) react to a mouse click.
  `FleetTabBar` raises `Chosen`, and the dashboard switches tabs unless it is
  busy or a dialog is open, the same guard its keys use.
- **Verified with the real binary** (outer fleetd running `fleet attach`
  against an inner fleetd with projects alpha and beta):
  - the picker listed *alpha — 2 panes* and *beta — 1 pane*, and Enter
    attached to alpha;
  - a click on `Subs (0)` in a real `fleet dash` moved the tab underline to
    Subs.
  - Tests cover the session listing and a click on the tab bar view.
## fleetd restores its session, 2026-09-29

A fleetd restart (a new build, a crash, `scripts\embedded.ps1 restart`) used
to lose every tab, split and agent pane. fleetd now keeps a snapshot and
rebuilds from it when it starts.

- **What is saved:** `<fleet config>\embedded-session.json`, or
  `embedded-session-<endpoint>.json` when `FLEET_ENDPOINT` is set, so test and
  second daemons never share one. It holds:
  - every workspace (hidden ones too) with its active tab;
  - per tab: its title, the layout tree (orientation and ratio), the active
    pane and the zoomed pane;
  - non-modal floats with their bounds and title;
  - per pane: cwd, args and the env it was spawned with, minus
    `FLEET_CLIENT`, which names a client that will be gone.
  - Modal floats (the fleet menu, approvals) and the old overlay workspace
    are left out: they belong to a moment, not the session.
- **When:** the render loop serialises the model (source-generated
  `SessionJsonContext`) at most once a second, and writes it only when it
  changed, through a temporary file and a move. An empty model deletes the
  file, so after fleetd exits because nothing is left (or `fleet quit`)
  nothing comes back; after a kill or crash, everything does.
- **Restore:** before the render loop starts, the snapshot is read (and
  copied to `.previous.json`, so a bad restore does not lose it), the model
  is rebuilt with new pane ids, and each pane is started again. A pane that
  fails to start is dropped and logged.
  - `AgentHarness.Resumed` picks up the conversation: bare `claude` gets
    `--continue`, the orchestrator's nvim gets its resume command, and nvim
    with `ClaudeCode` gets `ClaudeCode --continue`. Anything else runs as it
    was.
  - A pane whose program is `fleet` (the dashboard, `titled` wrappers) is
    started with the running fleetd's own executable, so a restart onto a new
    build does not relaunch the old one.
  - The project's own restore (`RestoreSessionHandler`, matching agents by
    worktree) sees the panes are there and adds nothing twice.
  - Focus comes back too: a workspace whose top float had the focus gets it
    back (`floatFocused`). If a menu was on top, restore skips it, and the
    tab gets the focus.
- **`scripts\embedded.ps1 stop`/`restart`** kill fleetd before its other
  processes; were a `fleet dash` pane to die first, fleetd could save a
  session without it.
- **Verified with the real binary** (scratchpad build, isolated config and
  endpoint): a split tab at 30/70, a float and a second workspace were
  saved, fleetd was killed hard, and a new fleetd came back with the same
  workspaces, tabs, float and pane programs; no pane shells were left
  orphaned. Tests cover the round trip, zoom, skipped modal floats, the
  resume mapping, per-endpoint files, and a daemon restart through the file.
## The menu's first frame is its last draw, 2026-09-29

The menu float stayed hidden until it had drawn at its fitted size, but it
was revealed on the *first* output after the fit that had content. After a
resize Terminal.Gui writes the screen in several passes (and sets its title),
and on a loaded machine those land in separate frames. So the menu showed a
partial draw and corrected it a moment later.

- A hidden float is now revealed only once its pane has also been quiet for
  `RevealWhenQuiet` (a daemon option, 60 ms; the daemon tests use 400 ms so a
  slow CI runner cannot pace the test out of the window). Every pass and the title have landed by then,
  so the first frame shown is the finished one.
- While a float waits to be revealed, the render loop wakes every 15 ms
  instead of 250 ms, so the wait adds about the quiet window and no more.
- `RevealAnyway` still reveals a float that never goes quiet.
- **Verified with the real binary** (a raw attach client recording every
  frame): each open sends a single frame with the whole menu, and nothing is
  redrawn after it. A later open appears after about 130 ms. A daemon test
  feeds eight quick passes and checks that the first frame showing the menu
  already holds the last one; it fails without the quiet window.
- **Screens opened from the menu** (Settings, dialogs, pickers, the
  dashboard) had the same problem one step later. The next window starts,
  draws at the float's old size (with a blank moment in between), and only
  then asks fleetd to fit the float, so it is drawn twice.
  - When a screen inside a float closes, `FloatScreens` now sends `hold`
    (`EmbeddedWiring.HoldOwnFloat`). fleetd keeps showing that float's last
    screen at its old bounds (`FloatState.Held`) and stops taking snapshots
    of the pane.
  - The next screen's `fit` resizes the pane behind the held picture. Once
    the pane has written after the fit and been quiet for `RevealWhenQuiet`,
    the hold ends, and the new screen appears at its new size in one frame.
  - A `fit` that changes the size of a visible float holds it in the same
    way, even without a `hold`. A hold that never gets its redraw ends after
    `RevealAnyway`.
  - Verified with the real binary: menu, then `S`. Settings appears 296 ms
    later as a single frame and nothing is redrawn after it. A daemon test
    (partial draw, resize, several passes) fails without the hold.
- **The warm menu showed a gap in that fix.** Terminal.Gui notices a
  console resize only when it polls for it, so it drew the next screen at
  the old size, went quiet (and was released), and relaid out ~370 ms later.
  - `fit` now returns the pane's real size, and a screen inside a float
    passes it straight to `IDriver.SetScreenSize` before it draws, so the
    first draw is at the right size.
  - `FloatScreens` holds on every screen change, not only on close, and fits
    before it sets the title. fleetd holds the float's border label with its
    picture (`FloatState.HeldLabel`), so a new title no longer lands on the old
    screen first.
  - Verified with the real binary: `Q` (quit dialog), `S` (Settings), `s`
    (switch project) and `l` (agents) each appear as one frame.
## Linux end to end, 2026-09-29

The Unix PTY and client had only unit tests. A run in WSL (Arch) used tmux as
the real terminal around `fleet` and `fleet attach`, with the CI-built
linux-x64 AOT binary and an isolated config and socket.

- **Found:** Terminal.Gui programs (the dashboard, the menu) drew nothing in
  Linux panes. Terminal.Gui 2.4 learns its size by sending `CSI 18t`
  (`SizeDetectionMode.AnsiQuery`) and waits for `CSI 8;rows;cols t`.
  libghostty-vt answers other queries (`6n`, `c`, `?u`) but not this one.
  Windows was unaffected, because there Terminal.Gui asks the console API
  through ConPTY.
- **Fix:** `PaneRuntime` answers `CSI 18t` itself with the pane's current
  size (`SizeQueries`, a byte matcher that survives a query split across
  reads), for panes that are not in win32-input mode.
- **Result:** 40 of 40 checks pass:
  - project picker, top bar, dashboard;
  - which-key, the menu float;
  - split, new tab, zoom, tab keys;
  - floats (new, typing, toggle);
  - a real terminal resize (SIGWINCH);
  - a mouse click on a tab, copy mode;
  - switching project from the menu;
  - detach (the tty is back to canonical with echo), reattach through the
    session picker;
  - restore after `kill -9` of fleetd (7 of 7 panes, the orchestrator
    resumed with `ClaudeCode --continue`).
## The fleet menu opens warm, 2026-09-29

Every menu open started a new `fleet menu` process. That meant process
start, ConPTY and Terminal.Gui init, the first draw, the fit and the settle:
about 250–310 ms, and about 840 ms for the first open.

- fleetd now keeps one **warm menu** per workspace a client shows (no hidden
  or overlay workspaces). It is a modal float that is `Parked`: started,
  fitted and drawn, but outside the view. It is not a focus candidate, not
  listed by `list-panes`, and not saved in the session.
  - Opening the menu unparks it, puts it on top and focuses it. If it is
    still drawing, the usual reveal applies.
  - Menus with an `--action` still start fresh.
  - The next warm menu starts when no menu is open in that workspace any
    more, at most once every 2 s per workspace.
- **Measured with the real binary:** the menu appears 11–48 ms after the
  key, instead of 250–840 ms.
- **Cost:** one idle `fleet menu` process per shown project workspace.
  - Parked menus don't keep fleetd alive (they're left out of the idle
    check).
  - A workspace left with only parked menus has them killed, so it is
    dropped.
  - The daemon option is `WarmMenus`, on in `EmbeddedWiring` and off in the
    daemon tests.
- **A warm menu is started before any client asks,** so it has no
  `FLEET_CLIENT`.
  - fleetd resolves a request from a menu pane without a client
    (`show`, `list-workspaces`) to the client that opened it
    (`ClientState.Menu`).
  - `EmbeddedDriver.ShowWorkspaceAsync` accepts a pane caller without a
    client.
  - `EmbeddedWiring.InsideClient` is also true inside any fleetd pane, so a
    warm picker never starts a nested `fleet attach`.
## fleet doctor and the embedded multiplexer, 2026-09-29

`fleet doctor` now reports on the embedded multiplexer, whichever driver is
chosen:

- whether this build links libghostty-vt;
- fleetd: its pid, workspaces, panes, warm menus and attached clients, and
  whether it runs this build or another one (restart to switch). fleetd has a
  `status` control op for this. The probe uses a driver without a start
  callback, so doctor never starts a fleetd just to look at it. A fleetd too
  old to know `status` is reported as running on an older build.
- the saved session: how many workspaces and panes it would restore, and when
  it was saved.

An unreadable session file is the one new *problem* (doctor exits 1): fleetd
would start empty. The rest is information. The feature gets it as a plain
model (`EmbeddedHealth`), gathered by `EmbeddedWiring.HealthAsync` in the
composition root, the way it already gets git's version.
## Windows and their projects, 2026-09-29

A *window* is one attached client, one terminal window. Each window keeps the
projects it has shown, most recent last (`ClientState.Projects`). Hidden-agent
workspaces and the overlay workspace are never projects. Two windows may still
show the same project (attaching from a second machine).

- **Quitting no longer leaves a dead window.** Before, the window drew an
  empty screen with "nothing to show" in the bar and never gave the prompt
  back. When a project's workspace goes away, every window showing it moves to
  its most recent remaining project. A window with none left is `Leaving`:
  fleetd sends it `Bye` with a reason, the client restores the terminal and
  prints `fleet: no project left in this window`, and the shell prompt is
  back. fleetd itself exits once nothing runs.
- Verified with the real binary (nested fleetd, `cmd` running
  `fleet attach`): quitting the shown project of two switched the window to
  the other one, and quitting the last one ended `fleet attach` with that line
  and the `cmd` prompt.
- **The quit question counts this window's projects.** `list-workspaces`
  now also reports, per workspace, whether it is `InWindow` (this window holds
  it) or `InOtherWindow`, and `ProjectLocation` carries both. On WezTerm,
  in-window means shown in this window, as before. With two or more projects
  in the window, quitting asks "Quit all in this window" or "Just <project>".
  Projects in other windows are never touched. "Quit all" quits the current
  project last, because the menu runs inside it and would otherwise kill
  itself halfway. Verified with the real menu: Enter quit both projects and
  sent the window home; Tab, Enter quit only the current one, and the window
  moved to the other.

### Switching projects between windows

- **The Switch project picker** (built-in multiplexer only) lists every
  project, including the current one, with where it lives: *this window*,
  *another window*, or *open*.
  - **A lower-case letter** (or Enter) brings the project into this window
    (opening it first if needed). `show` *takes* it: it leaves any other
    window, and that window moves to its neighbour or goes home.
    `fleet attach --project` still only shares a project.
  - **An upper-case letter** (or Shift+Enter) opens it in a new window. The
    `open-window` op releases it from every window, this one included (a
    window giving away its only project goes home), and sends this window an
    `open-window` host effect.
- **The window opens the new one**, because it runs in the user's actual
  terminal (`NewWindow.Plan`):
  - Windows Terminal (`WT_SESSION`): `wt -w new`;
  - WezTerm (`TERM_PROGRAM`): `wezterm start --`;
  - Linux: `$TERMINAL -e`, then `x-terminal-emulator -e`;
  - otherwise, on Windows, a new console window.

  The window runs `fleet attach --project <name>` (plus `--ssh <host>` when
  this window came over ssh).
- **The new window uses the same terminal profile as the window it came
  from:**
  - Windows Terminal: `wt -w new -p <WT_PROFILE_ID>`.
  - WezTerm: the running `WEZTERM_EXECUTABLE` with
    `--config-file <WEZTERM_CONFIG_FILE>`, so a pinned window (such as
    `.wezterm-rib.lua`) opens another pinned window.
  - Both terminals start the window from their own process, so the caller's
    environment doesn't reach it. `FLEET_ENDPOINT`, `FLEET_CONFIG_HOME` and the
    account-profile variables (`ACCOUNT_PROFILE`, `ACCOUNT_PROFILE_AUTO`,
    `CLAUDE_CONFIG_DIR`) are therefore set in the window's command.
- **`prefix w`** cycles only through this window's projects, in a stable
  order (a project is added when first shown, never moved).
- WezTerm mode is unchanged. Its driver refuses `OpenWindowAsync`, and the
  menu keeps the old picker there.
- **Verified with the real menu:** the picker labels the current project
  *this window*. Upper-case `B` sent `open-window beta`, and the window stayed
  on alpha. Launching the terminal is covered by `NewWindowTests`. The e2e
  runs deliberately don't open windows on the desktop.

### New panes open the default shell

A pane opened without a program (`prefix c` for a new tab, the splits,
`prefix f` for a float) used to start `COMSPEC`, which is `cmd.exe`. It now
starts the user's default shell (`DefaultShell.Resolve`, passed to fleetd as
`DaemonOptions.Shell`):

1. `FLEET_SHELL`, a command line;
2. Windows Terminal's `defaultProfile` command line, read from its
   `settings.json` (comments and trailing commas allowed; `%VARS%` expanded);
3. `pwsh -NoLogo` when pwsh is on `PATH`;
4. `COMSPEC`, or `SHELL` on Unix, as before.

Verified: a pane opened with no program started
`C:\Program Files\PowerShell\7\pwsh.exe` (the WT default profile) and showed
the user's own prompt.

### The dash tab is one framed window

The project's dash tab (the orchestrator and the fleet dashboard side by side)
used to show a frameless Claude pane next to a dashboard with its own rounded
border. It is now one window:

- **One rounded frame around the whole tab**, titled `fleet — <project>`. The
  divider between Claude and the dashboard joins it (┬ top, ┴ bottom; ├ ┤ for
  horizontal splits).
- **A tab is framed when it holds the dashboard** (`MuxModel.IsDashboard`: a
  `fleet dash` pane). `Arrange` lays its panes out one cell inside the content
  area, so pane sizes, mouse hits and focus moves all agree with what is drawn.
  Other tabs stay frameless and use the full width.
- **The focused side is lit:** frame cells next to the focused pane use the
  focus colour, and the rest stay dim, as dividers already did.
- **The dashboard draws no border of its own there:** fleetd sets
  `FLEET_FRAMED=1` for dashboard panes, and `FleetTheme.Screen` then uses no
  border. Its dialogs keep theirs.
- WezTerm can't draw across panes, so WezTerm mode is unchanged.
- **Verified with the real binary** through `fleet attach`: the frame and
  title, the ┬ join, and the dashboard without its own border inside it.
  `FramedDashTests` cover the layout, the joins and the focus colour.
- A blank line under the tab bar was tried and then taken out again at the
  user's request; content starts right under the bar.

### The agents list loads faster

*List agents* (and every dashboard refresh) waited on git before showing
anything:
- each agent's branch state is 2–4 git processes (`rev-list` against upstream,
  or `rev-parse` then `rev-list` against origin or the base, then
  `status --porcelain`), run one after another;
- the Agents and Subs lists each kept their own per-list cache, so every
  worktree was measured twice.

`BranchStates` now keeps one cache (fresh for 2 s; the dashboard refreshes
every 4 s) that the Agents, Subs and Repositories lists share. Before
building their rows, the lists `Warm` it: every worktree is measured once,
six at a time. The rows still appear only when they are complete, so there is
no first draw without branch state.

Measured on the user's real worktrees, the same git work took:

| Project | Worktrees | Before | After |
|---|---|---|---|
| fleet | 5 | 1.2 s | 0.35 s |
| pc | 11 | 3.2 s | 0.53 s |

### Glyphs whose width terminals disagree on

The dashboard's empty-list hint looked two columns short. That row starts and
ends with the pill caps, U+E0B6 and U+E0B4 (Nerd Font glyphs in the Private
Use Area):

- Terminal.Gui and fleetd's emulator both count them as 1 column (measured:
  in a pane, `a`, the cap, `b`, then a move to column 4 and `X` leaves
  `a·bX`, while 中 is 2).
- The terminal the client runs in may count them as 2. On Windows its ConPTY
  layer did, which pushed the rest of each such row out of place.

fleet can't choose how a host terminal measures a glyph. So `FrameEncoder` no
longer lets the host's cursor advance decide where the next cell goes after
such a glyph: after a wide cell, a Private Use glyph, or an emoji
(`WidthIsDisputed`), it positions the next cell explicitly. A terminal that
draws the glyph wider only affects that glyph, and never the rest of the row.
Plain text and box drawing are still written as one run.

## Claude profiles per project, 2026-09-29

Claude picks its account from `CLAUDE_CONFIG_DIR`. The user's PowerShell
profile derives that from the folder, using `~/.profiles.psd1`, and WezTerm
panes get it through a `cmd.exe` AutoRun hook. Panes under fleetd start
`claude` directly, so before this they all inherited whatever the shell that
started fleetd had.

- **fleetd now sets it per pane** from the pane's folder, through
  `DaemonOptions.PaneEnv`, wired to `PaneProfile.Env`. The rule is the
  profile's own:
  - the longest `Roots` entry that equals or contains the folder, else
    `Default`;
  - `CLAUDE_CONFIG_DIR` is that profile's `Claude` folder, and is left unset
    for `~\.claude`, Claude's own default;
  - a folder-derived value sets `ACCOUNT_PROFILE_AUTO=1`, so the user's shell
    inside the pane keeps following `cd`;
  - a pin or hand-set value inherited from fleetd's own shell is cleared,
    because fleetd serves every project.
- **A project can pin a profile.** `claudeProfile` in `projects/<name>.json`,
  set from *fleet menu > Settings > Claude profile* (`C`). It sets
  `ACCOUNT_PROFILE=<name>` (the same as `p <name>`) and that profile's
  `CLAUDE_CONFIG_DIR`, and it wins over the folder. It applies to panes
  opened from then on.
- An explicit `CLAUDE_CONFIG_DIR` in a spawn request still wins over both.
  Without a `~/.profiles.psd1`, panes keep the environment they get, as before.
- `~/.profiles.psd1` is read by a small data-file parser (`PowerShellData`:
  `@{}`, `@()`, quoted strings, `$true`/`$false`/`$null`, comments). It is
  reread only when the file changes.
- `fleet doctor` lists each project's profile and whether it is pinned or
  comes from the folder.
- **Verified** on the real file: all seven projects resolve as the PowerShell
  profile resolves them. An isolated fleetd started from a shell with the
  personal profile gave an Upskilling pane `.claude-rib`, a fleet pane
  `.claude-personal`, and a techweb pane no `CLAUDE_CONFIG_DIR` at all (work,
  the default).

## Notifications, 2026-09-29

A notice is an agent that wants the user: a question, a permission prompt, done
and ready for review, failed or its pane gone, stalled (the spinner with no new
output for 10 minutes), or branch trouble (`git merge-tree` finds conflicts
with its base, or it is 20+ commits behind).

- **The dashboard detects**, because it already reads every agent's pane text for
  the activity column. `NoticeDetector` is pure: it turns what the dashboard saw
  (`AgentWatch`) into notices. The base check runs in the background every 5
  minutes per worktree. A pane counts as gone only after it was seen alive, so a
  closed-and-not-reopened agent does not look crashed on startup.
- **One file per project** in `<config>/notices/<project>.json`, written only
  by that project's dashboard (the center writes dismissals). `NoticeSync.Apply`
  keeps a notice's start while its cause lasts, resolves it when the cause goes,
  and keeps resolved ones a day. A dismissed notice stays dismissed while its
  cause lasts and comes back as new only once the cause recurs.
- **Alerts fire on fresh notices only**, the open keys that were not open in the
  stored file, so restarting a dashboard does not alert again. The dashboard shows
  a toast (Windows: WinRT through `powershell.exe` under PowerShell's app id;
  Linux: `notify-send`). Under fleetd it sends `notices` with its open count
  and whether to ring; fleetd keeps the counts, draws `● here +elsewhere` inside
  the project pill (elsewhere counts only open workspaces), and sends a `bell`
  host effect to every attached client, which writes BEL. Under WezTerm the
  dashboard writes BEL itself.
- **The center** (fleet menu > Notifications, or a click on the pill) reads every
  project's file; bell and toast settings live in `_settings.json` beside them.
- `●` has a disputed width, so the frame encoder repositions the cursor after it:
  tests that read the client's byte stream cannot match `● 2` as one string.
## Remote machines, phase 1, 2026-09-29

Goal: a remote fleet's projects join the local one, in the same window, with the
local fleetd keeping the tab bar and the keys. Phase 1 is the link and the switcher.

- **fleetd owns the link.** `remote-connect <host>` makes the local fleetd run
  `ssh -T <host> fleet bridge` and speak the ordinary control protocol over it, as
  one more client of the remote fleetd (`RemoteLink`, over a `RemoteChannel` so the
  tests can use a second in-process fleetd). It reads `status` once (the remote's
  `Environment.MachineName` names the tab) and `list-workspaces` every 2 s; hidden
  agent workspaces are left out. `remote-disconnect` stops it; a dropped link turns
  the machine `failed` with ssh's last stderr lines, and `enter` retries it.
- **Prompts go through fleetd.** ssh runs with `SSH_ASKPASS=<fleet>` and
  `SSH_ASKPASS_REQUIRE=force`, so it never reads the terminal. fleet started with
  `FLEET_ASKPASS=<token>` in its environment is the askpass program: it polls
  fleetd's `askpass` op until the user answered in the Remote machines view
  (`remote-answer`), prints the answer and exits. A prompt with `yes/no` in it is
  shown in the clear, anything else as a password. Verified that Windows OpenSSH
  9.5 hands the host-key question to `SSH_ASKPASS`.
- **The switcher** (`SwitchTabs`, `FleetTabbedPicker`) shows All, this machine and
  one tab per connected remote. Choosing a remote project sends
  `open-remote-window`; the attached client opens a new window running
  `fleet attach --ssh <host> --project <name>`.
- **Next:** remote panes drawn by the local fleetd (phase 2), then the menu and
  agents, then notifications, as agreed.
## Remote machines, phase 2, 2026-09-29

A remote project in the same window. The cheapest sound design turned out to be to
leave the remote's layout code alone:

- **The link is an attached client of the remote.** `RemoteLink` says Hello with
  role `attach` over the ssh bridge, at the size of the local window, and keeps using
  the same connection for control requests (the remote's `Execute` already accepts
  requests on attach connections). The remote does everything as for any client:
  layout, splits, floats, copy mode, its own menu, and it sends ordinary frames.
- **The local fleetd shows those frames.** `show-remote` creates one workspace per
  machine, `@<machine>`, flagged `RemoteHost`, whose single pane runs over a
  `RemotePty`: frames from the link are its output, its resizes go back as `Resize`.
  A remote workspace is laid out full screen (`MuxModel.Area`), draws no local bar
  (the remote's bar is in the frame; only the local which-key badge is drawn over
  it), is never framed, and is left out of the session snapshot.
- **One bar that says where you are.** Hello carries a `label` and `set-label`
  changes it; the remote appends it to its project pill, so the bar reads
  `homelab @machine`. Clicks on the remote's tabs are the remote's to handle.
- **Input.** Keys, pastes and commands to a focused remote pane, and mouse events
  over it, are forwarded verbatim; the remote encodes them for its panes (rule 3).
  `switch-project`, `next-workspace`, `show` and `redraw` stay local, so `ctrl+s s`
  is the way back; the local switcher opens on the remote machine's tab when started
  from `@<machine>`. Remote clipboard and bell effects go to the windows showing it.
- **Switch project is handed back.** The remote's menu, opened in a labelled client
  (one viewed from another machine), asks its fleetd `hand-back switch-project`;
  the remote sends a `hand-back` host effect to the link, and the viewing fleetd
  opens its own switcher for the windows showing `@<machine>`. Without this the
  remote's switcher only knew the remote's projects and there was no way back.
- **Ending.** When the link ends the whole `@<machine>` workspace goes, including a
  local menu float opened over it; its panes keep running on the remote.
- **Limits.** One remote client per link, so one local window at a time follows a
  machine: two windows showing the same machine see the same project. Every key is
  a round trip to the remote. Both ends must run a build that knows `label` and
  `set-label` (phase 2 and later).
- **Tested** with two in-process fleetds: the remote's text and pill label reach the
  local pane, a typed key reaches the remote pane and not the local one, a split is
  made on the remote, `ctrl+s s` opens the local switcher for `@<machine>`, and
  disconnecting removes the view. Not yet exercised against a real ssh remote with
  the ghostty terminal in between.
## A window hears only its own projects, 2026-09-29

- **Scope is the window.** A client's window is `Showing` plus `ClientState.Projects`
  (what `MuxModel.InWindow` checks). The pill's "elsewhere" count sums only the other
  projects in that window; `notices` rings the bell only in windows that hold the
  project, and answers whether any window holds it. The dashboard shows a toast only
  when it does, so a project that sits in fleetd with no window stays silent. The
  notification center lists the window's projects (`ProjectsInWindow`) under All.
  Notices are still detected and kept for every project; only the alerting is scoped.
- **Stopping forgets.** `fleet daemon stop` deletes the saved session instead of
  saving it, and no later render tick may write it back (`_forgotten`), so the next
  start opens only the project you open. A crash or a restart without `stop` still
  restores from the file written every second.
- **The dashboard re-tells its count** every 30 s, so a fresh fleetd learns the
  counts without waiting for one to change.
## Sessions, 2026-09-29

A session is a named set of projects for one window, kept in
`<config>/window-sessions/<name>.json` (`sessions/` already holds the agent store).

- **Saving** asks fleetd `window` for the client's projects in window order
  (`ClientState.Projects` plus `Showing`); an `@<machine>` workspace is reported as the
  remote project its link is showing (`RemoteLink.Showing`) with the machine's ssh host.
- **Opening** happens only in the plain `fleet` picker, outside any client: it opens the
  local projects that are not running, starts fleetd if needed, reconnects each remote
  machine in a small window that can ask ssh's questions (`ManageRemotesView.ConnectAll`),
  and attaches with the session in the Hello (`window`, `showing`). fleetd furnishes the
  new client before its first frame (`Furnish`): each local project joins the window, each
  remote one goes through `show-remote`, and the saved project is shown. A project that is
  gone or a machine that cannot be reached is left out with a message.
- The picker only shows its Projects/Sessions tabs once a session exists, so `h` and `l`
  stay available as project accelerators until then.
## Remote machines, phase 3, 2026-09-30

Phase 3 was "the menu and agents for a remote project". Most of it came with phase 2:
the fleet menu (`ctrl+enter`) is forwarded, so in `@<machine>` it is the remote's menu
for the remote project, and its List agents is that project's dashboard, opening
agents on the remote. What was left were the places where the remote's menu reaches
outside the view:

- **The remote project quits.** The remote says goodbye to a client with no project
  left; the link now ends the local view on that `Bye` (`RemoteLink.EndView`), which
  closes `@<machine>` like any quit project, and gives the next `show-remote` a fresh
  `RemotePty`. The link itself stays connected.
- **"Open in a new window" on the remote.** The remote's `open-window` effect for the
  link becomes an `open-remote` effect for the local windows showing it, so a new
  local window attaches to that project over ssh.
- **Effects before goodbyes.** fleetd sent a leaving client its `Bye` before the host
  effects of the same frame, so a client moving its last project to a new window
  quit before it could open the window. Effects now go first.
## Remote machines, phase 4, 2026-09-30

Notifications from a remote project, under the window rule: a window hears only about
its own projects, and the remote project `@<machine>` is showing is one of them.

- **The remote serves its notices.** `list-notices` returns every project's notices
  from the remote's store (a `DaemonOptions.Notices` callback, so the daemon stays out
  of storage); `dismiss-notices` dismisses by the remote's own keys.
- **The link polls them** with the workspace list every 2 s. `RemoteLink.Fresh`
  compares open notices with the previous poll; the first poll only sets the baseline,
  so connecting does not alert about old notices.
- **The local fleetd** keeps the open count of the shown remote project as the notice
  count of `@<machine>`, so it adds to the pill's `+N` in windows holding it. A fresh
  notice of the shown project rings the bell in those windows and shows one toast, with
  this machine's bell/toast settings (`AlertSettings`, `Toast`); notices of the remote's
  other projects stay silent.
- **The center** asks `remote-notices` for the window's remote projects and shows each
  as `project @machine` (`RemoteNoticeView`). Dismissing maps the local notice key back
  to the remote's key (paths differ by OS) and goes through `remote-dismiss`; opening one
  shows that project here.
## Remote projects that are not running, and the way back, 2026-10-01

- **A remote's tab lists its saved projects.** `list-projects` returns the remote's
  project store (`DaemonOptions.SavedProjects`); the link lists those together with
  whatever runs there (`RemoteLink.Listed`) and keeps the running ones apart, which
  the switcher marks `open`. Showing a project that is not running sends
  `open-project` first: the remote runs `ProjectOpener.EnsureOpenAsync` (the same
  open-and-restore-agents path as the `fleet` picker) and the link waits up to a minute
  for its workspace before it shows it. A remote without `list-projects` still lists
  its running workspaces.
- **Warm menus hand back too.** The remote's menu is usually a pre-started (warm) float,
  started without `FLEET_CLIENT`, so `hand-back` never ran and its Switch project showed
  only the remote's projects. The menu now asks with just its pane; fleetd finds the
  client that opened that menu (`ClientState.Menu`) and hands the switcher back to the
  viewing machine.
## The notification center opens where you sit, 2026-10-06

In `@<machine>` the fleet menu is the remote's, so its Notifications (and a click on the
remote's notice pill, which the remote handles as `OpenMenu(client, notifications)`) opened
the remote's own center, which knew only that instance. It now follows the instance you sit
at, like the head chord (a `LocalCommands` entry) and Switch project:

- **`notifications` is handed back** with the same detection as `switch-project`
  (`FleetDaemon.HandsBack`): the remote's menu, in a labelled client or as a warm menu whose
  `ClientState.Menu` a link opened, asks `hand-back notifications`; the viewing fleetd opens
  `menu --project @<machine> --action notifications` for the windows showing it.
- **The center over `@<machine>`** runs without a local project and lists what it lists
  anywhere in that window: the window's local projects plus the shown remote project as
  `project @machine` (`remote-notices`, `remote-dismiss`), so `d` and `enter` work as before.
- **Unchanged** when nothing views the remote: an unlabelled client gets `Pending = false`
  and the remote opens its own center, and a local project's menu never hands back.
- **Tested** with two in-process fleetds: hand-back from a labelled client, from a warm
  menu and after a click on the remote's pill opens the center at home; a client attached
  to the remote directly and a local window are not handed back. The `fleet menu` process
  itself (the `MenuCommand` side) is not exercised by the tests.
## Known remote machines, 2026-10-02

fleetd keeps remote links in memory only, so a restart forgot every machine. The client
now remembers them; fleetd and the wire protocol are unchanged.

- **`remotes.json`** (`IKnownRemoteStore`, `JsonKnownRemoteStore`) holds per host (the ssh
  target, case-insensitive) an optional nickname and the last connected time. A missing or
  corrupt file reads as empty, like `keybinds.json`.
- **A host is remembered once it connects**: `ConnectFlowAsync` records it when the link
  reaches Connected, and the Remote machines view records any connected host it does not
  know yet (a link made before this existed). Disconnecting does not forget it; `x` does.
- **Nicknames resolve client-side.** `NicknamedRemotes` wraps `IRemoteMachines` and fills
  `RemoteMachine.Nickname`; `Label` (nickname, else fleetd's name) is what the switcher tabs,
  their `@machine` details and remote notice labels show. Matching still uses `Name` and
  `Host`, so a nickname never has to reach fleetd.
- **The view merges** live links with known hosts that are not connected
  (`RemoteEntry.Merge`, live first, then by last connected); the known ones are muted and
  `enter` reconnects them through the same flow.

## ctrl+enter and shift+enter on Linux, 2026-10-01

A Unix terminal sends Enter, ctrl+enter and shift+enter all as `\r`, so the attach
client could not see the menu key (or the newline key) and only `ctrl+s space` worked.
The client now asks the terminal for xterm's `modifyOtherKeys` level 1 on attach
(`CSI > 4 ; 1 m`, reset on detach). Level 1 leaves ordinary control keys (`ctrl+c`,
`ctrl+a`, ...) alone and reports the ambiguous ones as `CSI 27 ; mod ; key ~`, which is
what the `ctrl+enter`/`shift+enter` bindings now match (`KeyChord.Bytes`); terminals that
answer in the `CSI key ; mod u` form are normalized first. A report no binding wants is
turned back into the bytes the key would otherwise send (`ModifiedKeys.Legacy`), so a
program in a pane never sees the new form. Terminals without `modifyOtherKeys` keep
sending `\r`; `ctrl+s space` still opens the menu there.

The stop test also exposed a race: the render loop checked "not forgotten" and then
wrote the session, and `shutdown` could delete the file between the two, so the save
came back. The check and the write now happen under one lock with the shutdown's delete.
## Panes stay in their own project, 2026-10-01

Agents turned up in another project's workspace. With workspaces a "window" is a
project, and several paths worked out the target window the WezTerm way:

- **Opening an agent and dispatching** took the window of the *calling* pane first.
  From the notification center or a menu running in project B's window, project A's
  agent was moved or started into B.
- **Restoring agents and opening repositories** took the window of the first pane whose
  folder equals the project root, which can be a pane of another project.
- **Restored hidden agents** went to the shared `fleet-hidden` workspace instead of
  `<project>~hidden`, so quitting the project left them running there.
- **fleetd's `spawn`** let a request's window outrank the project it named.

Now every one of these asks `ProjectWindows.For`: with workspaces the answer is always
the project's own workspace (the WezTerm behaviour is unchanged); restore uses
`<project>~hidden`; and fleetd places a pane in the project a spawn names, whatever
window it carries, so a wrong window can no longer move a pane across projects. An
architecture test keeps features from deciding a project's window themselves again.

## Defaults follow the live keymap, 2026-10-02

The shipped defaults now match the keymap the user actually runs, so a fresh install
gets it without a `keybinds.json`: Keybinds `k`, Show log `l`, Permissions `p`, Switch
project `p`, Edit fleet config `e`, Settings `s` (were `e`, `L`, `P`, `s`, `E`, `S`).
Every menu still has unique keys; Switch project and Permissions share `p` but live in
the fleet menu and its Settings submenu.

The trade-off is accepted knowingly: `k` is also move-up, and a menu matches its item
keys before the list's motions, so in a menu that lists Keybinds `k` opens it instead
of moving up — the collision the move to `e` once fixed. The arrow keys still move.

## AIDLC engine, 2026-10

AIDLC used to be prose: with AIDLC on, dispatch pasted a five-step Plan/Implement/Test/
Review/Report text into the sub-orchestrator's CLAUDE.md, and nothing knew where a task
stood. Milestone 1 of the plan in `.fleet/orchestrations/ai-sdlc-research-plan` gives
it an engine core: fleet decides the process and keeps a record; the model carries it out.

**Profiles size the ceremony.** Five profiles, each a fixed set of stages with fixed
human gates (`Shared/Aidlc/ProfileCatalog`):

| Profile | Stages | Human gates |
|---|---|---|
| `express` (default) | Intake, Specify (short), Build, Verify, Review, Deliver | Deliver |
| `bugfix` | Intake, Discover, Specify (repro + expected), Build (failing test first), Verify, Review, Deliver | Specify, Deliver |
| `feature` | all nine | Specify, Plan, Build (walking skeleton), Deliver |
| `refactor` | Intake, Discover, Plan, Build (characterisation tests first), Verify, Review, Deliver | Plan, Deliver |
| `research` | Intake, Discover, Deliver (the report) | Deliver |

Stages have AI-DLC's six states (`pending`, `active`, `awaiting`, `revising`, `done`,
`skipped`) and units have their own nine. `Transitions` lists the allowed moves and
returns a failed `Result` for any other; a gated stage cannot go from `active` to `done`
without passing `awaiting`. `UnitGraph` validates a `units.json` plan (cycles, unknown
repositories and dependencies, duplicate ids and branches, acceptance-criteria coverage
through `Traceability`), computes the ready set (a walking skeleton runs alone first)
and reports `owns` globs that overlap between units of one repository that could run
at the same time. The glob check is conservative: when in doubt it reports an overlap,
because the cost is only that two units run one after the other.

**Settings, per project** (`AidlcSettings`, in the project's settings file; a missing
field means its default, so older files load unchanged):

- *Mode*: `off` (default), `on`, or `manual`. Manual used to mean "the prompt doubles the
  dispatch trigger" (`,,task`), which nobody found. It now means "only when the task
  starts with a profile prefix", such as `,feature: add oauth`. The `profile` argument of
  the `dispatch` MCP tool does not turn it on in manual mode; it only picks the profile
  when the mode is `on`. The prefix is the profile word and a colon; a colon keeps
  ordinary sentences that start with "feature" or "research" from being taken as one.
- *Default profile* (`express`) and *autonomy* (`guided`, or `automatic` to go on from
  unit to unit after the walking skeleton; failures still stop either way).
- Seven parts, all on by default: the spec, plan and deliver gates, the walking
  skeleton, and the verify, review and learn stages. A gate switched off keeps its stage
  but makes the gate automatic; verify, review or learn switched off skips the stage,
  recorded as `skipped` with the reason "off in settings". One pure function,
  `ProcessPlan.Resolve(profile, autonomy, off)`, turns this into the effective plan, so
  the record, the rendered text and the tests cannot disagree.

The settings are edited from the AIDLC item (`A`) in the menu's Settings submenu, which
now opens a screen with all of them instead of a three-way mode picker.

**Intake on dispatch.** When AIDLC applies, dispatch picks the profile (the prefix; with
the mode `on` and no prefix, the `profile` argument, then the project default), writes `state.json` (profile, autonomy,
stages with their states, units, created/updated) and starts `audit.jsonl` with
`IntentCreated`, `ProfileSet` (with where the profile came from) and one `StageSkipped`
per switched-off stage. Both go through `IIntentStore`; the JSON store uses the
source-generated `AidlcJsonContext`, writes `state.json` through a temporary file, and
appends one compact JSON object per audit line. Then it renders CLAUDE.md's
`## Process` from the effective plan: the stages in order, the artifacts each writes
(`discover.md`, `spec.md` with numbered AC-n, `design.md` and `units.json`,
`progress/<unit>.md`, `reviews/<unit>-<round>.md`, `learnings.md`), and which stages wait
for the user.

**Gates are honour-system until M3.** There are no gate tools yet, so the process text
tells the conductor to stop at each human gate, summarise the artifact, and wait for the
user's reply. The engine cannot enforce that yet; M3 moves gates onto the dashboard's
approval channel, where the conductor cannot answer its own prompt.

**`aidlc.md` is appended, not substituted.** A project's `.fleet/config/aidlc.md` used
to replace the process. With a generated, per-profile process, replacing it would throw
away the stage list and the gates, so the file now lands under `### Project guidance`
at the end of the process. Opening the fleet config used to seed `aidlc.md` with the old
built-in text; such a file would now append a contradicting second process, so a file
whose text is exactly the old default (`OrchestrationText.ClassicAidlc`) is ignored, and
the config folder no longer seeds one.

**Deferred.** M2: hook-based agent status (a sibling branch). M3: `aidlc_status` and
`aidlc_submit`, gates through the approval channel, the `GateWaiting` notice, refusing
illegal moves at the tool. M4: `aidlc_verify` with receipts keyed by commit SHA. M5:
`aidlc_plan_units`, unit agents with fleet-rendered briefs, the dependency scheduler
with its concurrency cap. M6: the read-only reviewer harness and the two-round review
loop. M7: the pipeline view, cost and metrics, and Deliver. M8: the learning loop.
Until then the record's later stages stay `pending`; only Intake is marked done.

## Main and sub-orchestrators in nvim, per project, 2026-10-02

Two per-project settings in *fleet menu › Settings › Fleet config* pick whether orchestrators
are hosted in nvim (on, the default and the old behaviour) or run as bare `claude` (off). They
are plain toggles there (see "The fleet menu as a tree" below):

- *Main orchestrator in nvim* (`v`): the project's own orchestrator, the pane in the project
  root that open project, switch/move project and rebuild dashboard start.
- *Sub-orchestrators in nvim* (`V`): orchestrators started by dispatch, and their
  restore/open.

Fleet already tells the two apart without a new marker: the main orchestrator is never an
agent record (it is the project-root pane), while every sub-orchestrator is a record with the
`orchestrator` harness whose worktree is its `.fleet/orchestrations/<slug>` folder. So the
project-root paths read the main setting, and the record paths read the sub setting. Each is
stored on its own (`mainOrchestratorInNvim` / `subOrchestratorsInNvim: "off"`) and only when
it differs from the default.

The nvim wrapper only carried the instruction pump, `:FleetTell`, ctrl+hjkl falling through
to WezTerm, and the session-persistence env. With a setting off, a bare orchestrator gets
the env through `SpawnOptions.Env`, resumes as `claude --continue`, and receives
instructions (tell_agent, a dispatch's kickoff) by send-text, the way the `claude` harness
does. The composition root reads the settings and hands a bool to the handlers;
`AgentHarness` stays free of storage. A running orchestrator keeps what it was started with;
a setting applies to the next launch.

Delivery follows how the running pane was started, not the current setting: dispatch,
restore and open store the host on the sub's `AgentRecord` (`InNvim`), and `TellAgentHandler`
types the prompt only for a sub started bare. Reading the setting at delivery time instead
double-delivered (pump plus send-text into nvim) or lost the message (a bare pane nobody types
into) once the setting flipped under a running sub. A record without `InNvim` was saved before
it existed, when every sub ran in nvim, so it counts as nvim. The daemon's session restore
relaunches a pane's own command, so the stored host stays true across a fleetd restart. A
mux-side signal was not used: panes carry no command in the `Pane` model, and WezTerm's pane
list does not report the process reliably.

The nvim pump delivers `.fleet/instruction.md` only once that pane's Claude is ready: when
fleet's MCP server answers `initialize` it touches `.fleet/claude.ready` in its working folder
(next to the ready marker in the fleet config), and the pump waits for a flag newer than its
own nvim start. A file younger than two seconds waits a tick too, so a rewrite within the same
second (`getftime` has one-second resolution) is not marked seen before it lands. With no flag
after 60 seconds (a Claude without fleet's MCP server) it delivers anyway. A dispatch writes the
kickoff and `instruction.seen` = `0` before it spawns the sub, so nvim never seeds `seen` from
the kickoff itself, and the hook no longer waits for the ready marker in nvim mode. Before this,
a sub whose Claude was slow to start (a cold remote) could get the kickoff typed into a screen
that was not taking input, or have it marked seen at boot, and sat "working" with no task. A
bare sub still waits for the ready marker (now up to 60 seconds, inside the hook's 90) and then
types the kickoff.

Rebuild dashboard re-creates the main harness with a split, so `SplitOptions` gained `Env`,
handled like `SpawnOptions.Env` (WezTerm wraps the command through `EnvLaunch`, the embedded
driver sends it to fleetd, which already started split panes with a request's env).

Not verified on a real machine: in a bare pane fleet installs no ctrl+hjkl mapping, so pane
navigation depends on the WezTerm config.

## Auto-close idle agents, 2026-10-02

Every open agent costs about 400 MB (claude, nvim, supermaven, a fleet MCP server,
conhosts), and most of them sit finished. A per-project setting (`autoClose`,
`autoCloseMinutes` in the settings file, off and 30 by default) lets fleet stop them.

- **The dashboard ticks it.** It runs right after notice detection in the
  dashboard's 4 s refresh, because that loop already lists the panes, reads the
  pane text and owns the notices. Each project has its own dashboard, and nothing
  on the dashboard closes it, so this covers several projects. fleetd is the
  only process that outlives the dashboards, but it is a pure multiplexer in
  `Platform`, and putting agent policy in it would break the layering. While a
  project's dashboard pane is gone, nothing in that project is auto-closed.
- **Activity is the later of two timestamps.** One is the start of the open
  `Done`/`Failed` notice: the dashboard stamps it within one tick of the report and
  persists it. The other is the last change in the pane's settled text
  (`NoticeDetector.Settled`, the same normalisation the stall check uses), so a
  user still talking to a finished agent keeps it open. Claude hooks would be a
  better signal, but fleet installs only the `UserPromptSubmit` dispatch hook,
  only for the main orchestrator. The text clock lives in memory, so restarting
  the dashboard restarts it, which errs towards keeping panes open.
- **The rule is pure** (`IdleAgents.ShouldClose`). It needs a reported `done` or
  `failed` status, a live pane, and idleness of at least the threshold. It refuses when
  the pane is working (`esc to interrupt`), waiting (`AgentActivity.Waiting`, or an
  open Permission/NeedsInput notice), active, or in the project root (the main
  orchestrator). "Active" is `Pane.IsActive` outside a hidden workspace, the only
  focus signal the mux port has. Under WezTerm it means the active pane of its tab,
  so an agent alone in its own tab is never auto-closed there. Fixing that needs a
  focused-pane query (`wezterm cli list-clients`) on `IMuxDriver`.
- **Closing is `StopAgentHandler`**, the same path as `m` > Stop and the
  `stop_agent` tool. The record stays (`Open = false`). Reopening a repo agent now
  passes its command through `AgentHarness.Resumed`, so claude starts with
  `--continue` the way sub-orchestrators already did. Before this, a stopped repo
  agent came back as a fresh conversation.

## Live agent status through hooks, 2026-10-02

Milestone M2 of the AIDLC plan: the hook contract in "Agent status and data flow" is
built, and notices no longer depend on reading pane text when an agent reports.

### Storage: daemonless files

`FileAgentStateStore` writes **one small JSON file per worktree and session** in
`<config>/status`, named `<sha256(path key)[..16]>-<session id>.json`. A write goes to a
uniquely named temp file and is moved over the target, so two hook processes of one
session never share a temp file and a reader never sees half a file. Readers aggregate
on read; a corrupt file is skipped; a report older than a day is dropped and its file
deleted. `SessionEnd` deletes the session's file, and **`SessionStart` deletes every
other session's file for the same folder**: a pane that was killed or crashed never
sends `SessionEnd`, and without this its last Working or Blocked report would outrank
the new session for a day. One Claude per agent folder is the normal case, so this
costs nothing in practice.

On Windows a file another process has open cannot be replaced, and the dashboard
reads these files every 500 ms. The replace and the delete therefore retry for up to a
second (as `BusyFiles` does elsewhere) instead of giving up at the first collision, a
failed write removes its temp file, and readers open with `ReadWrite | Delete` sharing
so a `SessionEnd` delete is never refused. Temp files older than a minute are cleaned up
on read.

Why files and not a state daemon: no driver needs a background process for status;
fail-silent comes for free (a missing file is an unknown agent); and one file per
session needs no read-modify-write, so concurrent hooks cannot lose each other's
reports. The cost is polling: the dashboard reads the folder at most every 500 ms.
The WezTerm Lua `state.json` writer is still not needed and still not designated.
`IAgentStateStore` stays the seam, so a daemon remains a same-day swap.

### The hook

`fleet hook` (no flags) reads the payload Claude Code sends on stdin. It keys the report
on `CLAUDE_PROJECT_DIR`, the folder Claude was started in (fleet starts it in the agent's
worktree or orchestration folder), and falls back to the payload's `cwd` only when that
variable is missing: `cwd` follows the session's `cd`, and a session that moved into
`src` would otherwise write a second file and leave a stale one behind. It also uses
`hook_event_name`, `session_id`, `transcript_path` (stored with each
report, ready for cost totals), `agent_id` and `notification_type`. It does not resolve
the project or load settings, so it stays a stdin read and one file write. Every
failure is swallowed into the log and the exit code is always 0: a hook can never
block Claude.

The contract as written, with four refinements that follow from what the payloads
carry and from running it:

- **PostToolUse → Working** (added after review). A Blocked report otherwise lasts until
  the next `PreToolUse` or `Stop`, which after a long tool can be a long think away.

- **Notification** is `Blocked` except `idle_prompt` (Claude's "still waiting" reminder
  after a turn ends), which is `Idle`, and `auth_success`, which reports nothing. A
  `Blocked` report records whether it waits on a **permission** (`PermissionRequest`,
  or `notification_type: permission_prompt`) or on **input**, so the notice can say
  which.
- **SessionEnd** clears the session's report (Unknown) rather than leaving it Idle.
- **SessionStart with `source: compact`** reports nothing: auto-compaction fires it in
  the middle of a turn, and the agent goes on working.
- **Subagents** (`agent_id` present): Working and Blocked still count, since a
  subagent's permission prompt is the parent's too, but anything that would report
  Idle or clear the session is dropped. `SubagentStop`/`SubagentStart` are not in the
  contract and report nothing. This is the invariant "a subagent never marks the
  parent done".

`Shared/Hooks/HookStatus` is the contract (event to state, pure); `Shared/Status`
holds `AgentState`, `AgentReport` and `AgentStatusRules` (Aggregate, Derive,
MoreUrgent, plus `For(snapshot, worktree)`, which aggregates every report whose cwd is
the worktree or inside it). Aggregate is "the most urgent derived report", so
Aggregate and the dashboard sort are one ordering.

### Wiring

`ClaudeConfigWriter` writes the hook into `.claude/settings.local.json` for agent
worktrees (`SyncWorktree`) and for the project root and orchestration folders
(`Sync`), on all eight contract events, in Claude Code's exec form
(`"command": <fleet>, "args": ["hook"]`, 10 s timeout) so no shell parses the path.
A hook is fleet's when its command is the fleet executable (file name `fleet`, any
folder) and its first argument is `hook`, so a user's `git hook …` entry is never taken
for fleet's; re-syncing replaces fleet's
entries and leaves the user's own hooks on the same events, matchers and unknown keys
alone. The dispatch hook on `UserPromptSubmit` is a separate entry and unaffected.
`fleet doctor` prints, per project, whether status hooks are on and in how many of
its agents they are wired.

The hooks are synchronous rather than `async: true`: an async `PreToolUse` and the
`Stop` after it could finish out of order and leave a finished agent "working". The
cost is one short process per tool call (about 75 ms for the JIT build, less for AOT).

### Setting

**Live status via hooks**, per project, default on (`SettingsConfig.StatusHooks`, an
init property so the record's positional constructor is untouched; stored only when
off). Off: fleet removes its status hooks the next time it syncs an agent's settings
and the dashboard ignores hook state for that project. The settings editor toggles it
with `enter`. Saving from the dashboard resyncs agent worktrees as it already did for
permissions; orchestration folders pick it up when next dispatched.

### Dashboard and notices

When an agent's pane is alive and a hook report exists, the row shows the aggregated,
derived state (`waiting` for Blocked, `stalled`, `working`, `idle`). `NoticeDetector`
gets the report on `AgentWatch.Hooked`: Blocked on a permission raises **Permission**,
Blocked on input **NeedsInput**, and Working past 600 s **Stalled**. With no report, the
pane-text checks run exactly as before, so harnesses without hooks and agents started
before this version keep working. Done, failed, pane lost and branch notices are
unchanged.

Two states no hook can end, so the pane confirms them (it is read only for these, so
Working and Idle agents are never scraped):

- **Stalled needs the busy spinner.** Esc interrupts a turn without a `Stop`, leaving
  the last report Working. If the pane shows no `esc to interrupt`, the agent is idle at
  the prompt: the row says idle and no Stalled notice is raised. If it shows a permission
  or question prompt instead (a Blocked report that never landed), the agent is Blocked:
  the row says waiting and the Permission or NeedsInput notice is raised.
- **Blocked ends when the approved tool runs.** No hook fires when you approve a
  permission; the next one is `PostToolUse`, after the tool. If the pane shows the
  spinner and no prompt, the row says working and the permission notice resolves. A
  permission prompt dismissed with Esc sends no hook either: a permission block whose
  pane was read and shows neither the prompt nor the spinner is idle. A block on
  **input** is not released this way, since elicitation dialogs and other attention
  notifications do not reliably show the wording fleet looks for, and hiding a real
  question is worse than a stale "waiting".

A pane not read yet keeps the reported state: a Blocked report still raises its notice,
and a Stalled one does not.

### Rejected

- **Async hooks**, for the ordering reason above.
- **One file per worktree** holding every session: needs read-modify-write across
  concurrent hook processes.
- **Walking up from `cwd` to the folder holding `.git`** to find the key. Orchestration
  folders are not repositories, and the project root may be one, so it would key them
  wrongly; `CLAUDE_PROJECT_DIR` names the right folder directly.
- **Trusting hook state alone for Stalled and Blocked**, for the two cases above.

### Not verified

- A dashboard watching a real agent through a permission prompt and a stall.
  Verified: exec-form `args` against Claude Code 2.1.287's hook schema; a headless
  `claude -p` run in a scratch folder with these hooks, which left an `Idle` report
  from `Stop` carrying the session's cwd, id and transcript path (and none when
  `SessionEnd` was wired, as intended); a second such run with a probe hook showing
  that exec-form hooks receive `CLAUDE_PROJECT_DIR`; and `fleet hook` fed by hand
  (Blocked with its reason, garbage stdin exits 0).
- The spinner and prompt texts the pane checks rely on (`esc to interrupt`,
  `do you want`, `no, and tell claude`) are Claude Code's current wording, the same the
  pane-only detection already used.
- The `cwd` Claude reports is the long path; a worktree recorded under an 8.3 short
  name (`REDMER~1.NAU`) would not match it. fleet records full paths, so this is
  noted rather than handled.

## The head orchestrator, 2026-10-02

One Claude above every project's orchestrator, opened by a global chord.

- **Chords.** `alt+o` and `alt+shift+o`, direct (no prefix), are two new keymap actions
  (`OpenHead`, `OpenHeadVoice`) in their own Keybinds group, *anywhere, no prefix*, so
  they are rebindable like everything else. `fleet apply-keybinds` emits them into
  `fleet.lua`. In a window without a fleet dashboard they are forwarded to the pane, as
  the prefix is. In the head's own workspace the chord of the running mode hides it and
  the other one switches the mode (below).
- **No float on WezTerm, so a workspace.** WezTerm has no floating panes. The closest
  equivalent that keeps the session alive is a workspace of its own, `fleet-head`:
  `SwitchToWorkspace` with a `spawn` creates it with `fleet head` the first time and only
  switches afterwards, and the chord in the head switches back to the workspace it came
  from (`wezterm.GLOBAL.fleet_head_return`, which survives config reloads). A hidden head
  keeps running, and workspaces are GUI-wide, so the head is one session for every
  project and window. A separate OS window was the alternative. It was rejected because
  WezTerm can only minimise a window (`Hide`), and refocusing a minimised window is
  platform-dependent.
- **Voice.** Claude Code has no CLI flag for voice. Its dictation is the setting
  `voice.enabled` (older: `voiceEnabled`), which `/voice` writes to user settings and
  which also needs a claude.ai login and the `allow_voice_mode` flag. This was found by
  reading the 2.1.287 binary. `fleet head` starts `claude --settings <file>` with
  `{"voice":{"enabled":true}}` for the voice chord and `false` for the plain one, so
  `alt+o` means typing mode even if the user enabled voice globally.
- **Switching mode, 2026-10-04.** Each chord means its mode for a running head too: the
  chord of the running mode shows or hides the head, the other one restarts it in the
  new mode, shown. In-session switching was checked first and rejected. Claude Code
  2.1.289 has an explicit `/voice [hold|tap|off]`, so the state would not have to be
  guessed, but it saves to `userSettings`. The head's `--settings` file is flag settings,
  which outrank user settings, so the write would not take effect in the head and would
  turn voice on or off for every other Claude session of the profile. (The precedence is
  Claude Code's documented order, not tested against a running head.) A settings file
  rewritten under a running Claude is not re-read either. So fleet kills the head and
  starts a new `fleet head [--voice]`, which passes `--continue` (the started marker
  exists) and keeps the conversation. A turn in progress is cut off; waiting for idle
  would need the head MCP server's readiness check to reach the multiplexer, and an
  explicit chord press was judged enough. The mode is known without asking Claude:
  fleetd reads `--voice` from the head pane's command (`MuxModel.IsVoiceHead`), and
  `ToggleHead` answers `OtherMode`. On WezTerm, `fleet.lua` keeps it in
  `wezterm.GLOBAL.fleet_head_voice`. There, the new head splits off the old pane so the
  `fleet-head` workspace never empties, and the old panes are killed with `wezterm cli
  kill-pane` from `background_child_process`; a blocking `run_child_process` would wait
  on the GUI that runs the callback. A head started by an older `fleet.lua` has no
  recorded mode and counts as text.
- **Persistence.** `fleet head` runs Claude in `<fleet config>\head`, writing `CLAUDE.md`
  (the brief), `.mcp.json` (`fleet mcp --head`), the head tools pre-approved in
  `.claude/settings.local.json`, and folder trust, every start. After the first start it
  passes `--continue`; if that exits non-zero within five seconds (no conversation to
  continue) it starts fresh.
- **`fleet mcp --head`** is a second tool set on the same `fleet` server name, in its own
  slice (`Features/Head/ServeHead`): `list_projects`, `switch_project`, `menu_action`,
  `list_agents`, `relay`, `tell`, `show_agent`, `hide_agent`. Opening, switching and
  dashboard handover reuse
  `ProjectOpener`, `LocateProjectHandler` and `fleet request`'s store through the
  composition root. On WezTerm, switching focuses the project's dashboard pane and asks for
  its workspace through the workspace request file. On a multiplexer with workspaces it
  shows the workspace.
- **Permissions.** Inside project X the head is gated by X's own settings: `relay` is X's
  `dispatch` rule, `tell` X's `tell_agent` rule (a plain message is not a dispatch) and `list_agents` X's `list_agents` rule. *Ask* always goes to X's
  dashboard dialog (`IApprovalChannel`), whatever channel the rule names, because the
  head's Claude has one permission rule per tool, not per project, so Claude's own prompt
  could not honour X's choice. Navigation needs no permission.
- **Relay.** The main orchestrator is the pane in X's root that is not the dashboard
  (`DashPaneMarker`), not hidden and not in `fleet-head`. The relay sends the prompt with
  X's trigger in front, and after 400 ms Enter, as raw keys (`send-text --no-paste`). When
  X's *Main orchestrator in nvim* setting is on, `Ctrl-\ Ctrl-N` then `i` go first, to take
  nvim from any mode into its Claude terminal; when it is off, Claude gets the keys directly.
  The setting is read at delivery time, so a pane started before the setting flipped gets
  the wrong form until the project is reopened. `tell` shares the path but sends the prompt
  as-is, with any leading trigger stripped, so the orchestrator's hook never dispatches it.
  The orchestrator's `UserPromptSubmit` hook then dispatches exactly as for a typed
  prompt. *Ready* is read from the screen: two samples a second apart that are identical,
  with no spinner (`esc to interrupt`), no question or permission prompt, and a Claude
  input marker (`? for shortcuts`, `shift+tab to cycle`, `❯`, `> `). A closed project is
  opened and waited on for up to 90 s. A busy one gets the prompt queued in the MCP
  server, per project and in order, and a pump delivers it when idle (polling every 2 s,
  giving up after an hour without progress). The queue is in memory: it dies with the
  head's Claude.

**On the built-in multiplexer, a float that moves.** fleetd has floats, but a float
belongs to one workspace. Panes are global to the model, though, and a float is only a
`FloatState` in a workspace's list, so moving the entry moves the float without touching
the pane or its process. The head is that float:

- `alt+o` sends the `head` command (`head voice` for `alt+shift+o`). The attach client
  reads the chords from the fleet keymap (`EmbeddedWiring.HeadKeys`), so Keybinds rebinds
  them here too. They are merged over fleetd's default direct keys, and
  `embedded-keys.json` still wins over them. A chord that does not parse is dropped
  rather than discarding the whole keys file.
- `MuxModel.ToggleHead`: with no head pane (one whose command is `fleet head`), fleetd
  spawns one as a modal float, 80% of the screen, in the workspace the client shows. If
  the head float is in that workspace, it moves to `fleet-head~hidden`, a holding
  workspace whose `~hidden` suffix keeps it out of every project list. If it is anywhere
  else, it moves here, on top, with the keys. If the head runs in the other mode, it
  answers `OtherMode` without moving anything, and fleetd kills the head pane and
  spawns a new one in the chord's mode, here.
- Modal, because a modal float is drawn whether or not the workspace's floats are shown,
  and is never counted, toggled with `prefix t` or tiled. So the head is independent of
  the ordinary floats. It is not saved in the session snapshot either: after a fleetd
  restart the next `alt+o` starts a fresh `fleet head`, which resumes its conversation.
- The head pane gets `FLEET_CLIENT`, so its `fleet mcp --head` shows workspaces on the
  client that opened it. `switch_project` leaves the head float where it was. Pressing
  `alt+o` in the new project brings it along.

Quitting a project while the head is shown in it closes the head with that workspace's
panes. Its next start resumes the conversation.

## Managing sub-orchestrators over MCP, 2026-10-03

Until now only the dashboard's Subs tab could stop or remove a sub: `list_agents`
(`ToolText`) filters orchestrators out, so an orchestrator could neither see nor address
one. Three tools close that gap: `list_subs`, `stop_sub(slug)` and
`remove_sub(slug, delete_folder = false, remove_agents = false)`. They are `HarnessTool`
values like every other tool, so they get permission rows and Claude rules for free.
`list_subs` is a read (allowed); the other two ask. They are not in the sub-autonomous
set (`McpGate.SubAutonomous`): stopping or removing *another* sub is not routine work
for a sub.

- **Listing** (`ListSubs/SubSummary`) reuses `SubTree`. "Pane open" is measured from the
  live panes, as `tell_agent` does, not from the stored `Open` flag. The last report's
  summary and time were never kept, only the status, so `ReportStatusHandler` now also
  stores `Summary` and `ReportedAt` (ISO 8601) on the `AgentRecord`. Older records show
  no report line until the next report.
- **Stopping** is `StopAgentHandler`, unchanged: it kills the panes in the sub's folder
  and keeps the record (`Open = false`) and the folder.
- **Removing** is `RemoveSubHandler`, in the `Agents/RemoveAgent` slice beside
  `RemoveAgentHandler`, whose orchestrator branch (discard the folder rather than a
  worktree) it reuses. It lives there and not in `Orchestrations` because a slice may not
  reference another, and the removal *is* `RemoveAgentHandler`. It refuses a sub whose
  status is still `working` and the caller's own sub. A sub that never reported counts
  as working; the Subs tab is the way out for one that died silently. The sub goes first,
  so a busy folder fails the call before any child is touched.
- **The children.** By default they are left registered and running, with `Owner`
  cleared, so they become top-level agents. Keeping the owner would leave them in an
  orphan group on the Subs tab and let a later sub with the same slug adopt them (and
  cascade over them on its own removal). Clearing it also pins `Claude` to the agent's
  current `RunsClaude`, because a record without an explicit `Claude` derives it from
  `Owner`. `SubChildren` holds both the lookup and the release, and the dashboard's
  removal uses them too: declining "throw away its agents" now moves them to the Agents
  tab instead of leaving them as orphans.
- **`remove_agents`** removes each child with its worktree (the branch is kept, as
  always), but first refuses one with uncommitted changes (`InspectAsync`) or with
  commits on no remote and not on its base (`UnpushedAsync`:
  `git rev-list --count HEAD --not --remotes <base>`, falling back to dropping `<base>`
  if it doesn't resolve). If the count can't be read, the child is kept. A refused child
  is released like an untouched one and named in the result, which never fails because
  of a child.
- The head is unchanged. It reaches a project only through `relay` to that project's
  orchestrator, which now has these tools.

## No folder picker where its pane can't be seen, 2026-10-04

Running `fleet` from a plain terminal on `embedded`, then *New project* → *browse*,
froze the picker. The picker runs outside any fleetd pane there, so `SpawnHereAsync`
fell back to `spawn` and yazi opened in a fleetd workspace no client was showing.
`PickFolder` then blocked the UI loop polling for a choice nobody could make, until
its ten-minute ceiling.

The same freeze hit inside fleetd, which is how it was reported (embedded, in
WezTerm). Opening fleet on no project runs the picker as the client's overlay: a pane
in `fleet~overlay` that `list-panes` leaves out. The picker does have a current pane
there, so browse asked for a float, but `CallerWorkspace` found no workspace for the
overlay pane and the float fell through to `default`, which no client was showing.
`DaemonTests.A_float_asked_for_from_the_overlay_lands_where_no_client_draws_it` pins
this down. It was not a sync-over-async deadlock: every await under the embedded
driver carries `ConfigureAwait(false)`.

`Adapters.CanShowPaneHere` says whether a spawned pane reaches the user: always on a
multiplexer without workspaces (WezTerm opens a tab or window), and on one with
workspaces only from a pane it lists, which rules out both no pane and the overlay.
From a project's pane (the menu float's *New project*) and on WezTerm nothing changes.

Where it is false, *browse* runs yazi in the picker's own terminal instead. Terminal.Gui
can't lend the console mid-dialog (its input thread would eat yazi's keys, and
`Driver.Suspend` is SIGTSTP), so the form hands back a `ProjectDraft` of what was
typed and closes. The picker stops too, its `IApplication` is disposed, yazi runs
with the inherited console, and a fresh application reopens *New project* filled
in with the name and the chosen folder (or the typed root if yazi was cancelled).
Escaping that form falls back to the picker. Disposing and starting a second
`IApplication` in one process was already done here (`PromptTakeOver`).

## The head lists projects per machine, 2026-10-04

The head could not tell which machine a project lives on. `list_remote_projects` is a new
head tool rather than more output from `list_projects`: every other head tool takes a
*local* project name, and `list_projects`' queue counts and roots only mean anything here,
so mixing remote projects into it would invite relaying to a project the head cannot reach.

- **Sources.** `HeadDeps` gains `IRemoteMachines` (the nicknamed fleetd links, the same
  list Switch project's machine tabs use) and `IKnownRemoteStore` (`remotes.json`). Both
  only read: `list-remotes` asks the running fleetd, and with no fleetd the list is empty.
  No ssh connection is opened, so nothing can prompt for a password.
- **Shape.** `this machine` first, each project `open` or `closed` (`IsOpen`, as for
  `list_projects`); then each live link in fleetd's order, as `nickname (host)` or just the
  host, with its state; a connected one lists its saved projects (`Projects`, `open` when
  in `Running`), as the switcher does. Then the known hosts with no link, newest first, as
  `known · not connected`. A link that is connecting, asking or failed shows its state and
  no projects: fleet keeps no last-known project list for a machine.

## New project from the switcher, 2026-10-04

Connecting to a remote with fleet running but no projects left nothing to do: its
machine tab was empty and the startup picker's *New project* only works on this machine.

- **Entry, not key handling.** `SwitchTabs.For` appends `+  New project...` to *this
  machine* and to each machine tab, keyed by the keymap's `new-project` text (`n`), and a
  `SwitchTarget` with `IsNew` (and the machine's `Host`). Explicit keys win over
  accelerators in `PickerKeys`, so `n` reaches it without `FleetTabbedPicker` changing.
  *Open* and *All* get none: they are about existing projects, and a new one needs a machine.
- **This machine** reuses `CreateProjectView` with `PickProjectCommand.FolderPicker`, then
  opens the project through `SwitchByWorkspace` (or a new window on `SHIFT`).
- **A remote.** `IRemoteMachines.NewProjectAsync` sends `new-remote-project`; the local
  fleetd shows the `@<machine>` viewer (`ShowViewer`, split out of `ShowRemote`, which needs
  no project) and `RemoteLink.NewProjectAsync` sends the remote the `menu` command with
  `new-project`, as if typed there. The remote runs `fleet menu --action new-project`,
  which now opens the New project form straight away (cancel falls back to its picker).
  The project, its folder and its orchestrator all live on the remote.

## The head shows a project's structure, 2026-10-04

The head had no way to list a project's repositories, and `list_agents` is flat: it cannot
say which agents a sub-orchestrator started. `project_structure(project, remote)` is one
new head tool rather than a head `list_repositories` plus a second agents listing, because
"what does X look like?" wants all of it at once and one call is one approval round.

- **Reuse, not a second copy.** The head slice may not reference `ListRepositories` or
  `ListSubs`, so `HeadDeps` gains a `Structure` delegate. The composition root's
  `ProjectStructureReader` fills it from `ListRepositoriesHandler`, `SubSummary.Text` (the
  `list_subs` text, sub → its agents) and `SubSummary.Unowned` (`SubTree`'s board: agents
  with no owner). `list_subs` and the reader share the pane check (`PaneOpenAsync`).
- **Last reports.** A repo agent's `report` was stored but shown nowhere over MCP;
  `SubSummary` now prints its `last report` line under the agent, as it does for a sub.
- **Shape.** `name  root`, then `repositories`, `sub-orchestrators` and `agents not under a
  sub-orchestrator`, each with its lines indented. Agents whose sub is gone stay under the
  sub's old name, as in `list_subs`.
- **Permissions.** Each part goes through its own project rule (`list_repositories`,
  `list_subs`, `list_agents`); a refused part shows the refusal in its place, as
  `list_agents` does per project.
- **Remote.** Like `list_agents`: the origin forwards it over the fleetd link and the
  remote's `ServeOriginAsync` answers it from its own stores. An older remote fleet
  answers that it does not serve the tool.

## The head shows and hides named panes, 2026-10-04

The head's only way to hide a pane was `menu_action` `toggle-hidden`, which acts on
whatever the dashboard has selected. `show_agent` and `hide_agent` name the target instead.

- **Addressing.** `repository` + `branch` for an agent, `sub` alone for a sub-orchestrator
  (its slug, the record's `Branch`), `sub` + `repository` + `branch` for an agent whose
  `Owner` is that sub. Two tools rather than one with a `visible` flag: "show X" and
  "hide X" are what the user says, and each reads as its own action in the head's
  permission list. An unknown name is an error listing what there is at that level.
- **One mechanism.** `HeadVisibility` (in the head slice) resolves the target, decides the
  no-op cases and gates on the project's own rules: `set_agent_visible`, or `open_agent`
  when nothing is running. The work is a `HeadDeps.SetVisible` delegate built in the
  composition root (`HeadPanes`) from the same handlers the dashboard toggle and the
  project tools use: `HideAgentHandler` when the agent has panes, `OpenAgentHandler`
  when it has none, as the dashboard's hide key does. The project's window comes from
  `ProjectWindows.For` without preferring the caller, because the caller is the head's
  float or workspace, not the project; `OpenAgentHandler` gained `preferCaller` for that.
  Focus goes back to the pane that had it, as in the dashboard.
- **No-ops.** Show is a no-op when the agent has a pane and is not hidden; hide when it is
  hidden or has no pane. Both succeed and say so; neither asks for permission.
- **Remote.** The tools go over the existing `HeadAsync` link like `relay` and
  `list_agents`; the remote's head serves them through `ServeOriginAsync`, so the remote
  project's permissions apply there. Neither tool switches the terminal to the project.

## The fleet menu as a tree, 2026-10-06

The menu outgrew two flat lists: Settings had eleven unrelated entries, and session and
remote entries sat on the top level next to everyday ones. It is now three levels:

```
fleet menu                     settings                          settings › fleet config
Q  Quit fleet                  ── session ──                     v  Main orchestrator in nvim  [on]
m  Go to dashboard             w  Save window as session         V  Sub-orchestrators in nvim  [off]
p  Switch project              r  Remote machines                i  Auto-close idle agents     [30m]
l  List agents                 ── configure ──                   A  AIDLC settings              ›
e  Open editor here            c  Fleet config               ›   C  Claude profile              ›
f  File navigator              k  Keybinds                   ›   e  Edit fleet config file
n  Notifications               ── maintenance ──                 ── permissions ──
s  Settings                ›   b  Rebuild the dashboard          p  Permissions                 ›
                               x  Clean up stale agents
                               l  Show log                   ›
```

- **One tree, in `FleetMenus`** (`Features/Menu/ShowMenu`): each menu is a list of sections
  (an optional header plus actions), and a submenu is an action (`OpenSettings`,
  `OpenFleetConfigMenu`, id `fleet-config-menu`) that maps to its sections. `MenuCommand.Parent`
  looks an action up in that tree, so `bksp` goes up one level from any depth; before, it was a
  `Contains` check that only knew Settings. `KeymapGroups` follows the same three groups, so the
  keybinds screen reads like the menus; Open editor stays in the dashboard group, its first home.
- **Headers are rows, not items.** `FleetRowSource` takes `headersBefore`; a header maps to the
  item after it and `Holds` is false for it, so `KeepOffSpacers` steps over it like a gap and
  item indices stay what the caller passed in.
- **Toggles flip in place.** The two nvim settings show `[on]`/`[off]`; `enter` or their key
  flips the setting, saves it and redraws the row, and the menu stays open. The `FleetPicker`
  float they opened before is gone. Auto-close shows `[off]` or `[30m]` but keeps its dialog,
  because turning it on also asks for minutes.
- **`›`** marks the entries that lead to another menu or screen (Settings, Fleet config,
  Keybinds, Show log, AIDLC, Claude profile, Permissions).
- **Keys are unique within a menu**, checked by a test over every menu. Clean up moved from `c`
  to `x` to free `c` for Fleet config; overrides in the keymap file keep working because no
  action id changed.
- **Open editor here only shows when it can work.** Before drawing the top level, the menu asks
  `OpenEditorHandler.CanOpenEditorAsync` (a usable multiplexer, then `Caller` over the pane
  list) and leaves the entry out when the menu was not opened from an agent or sub-orchestrator
  pane. The action uses the same `CallerAsync`, and keeps its error dialog for a pane that went
  away between drawing the menu and choosing the entry. It costs the top level one
  `ListPanesAsync` before it opens; the driver is fail-silent, so a dead mux only hides the entry.

## Session names and per-role models, 2026-10-06

Every pane started as plain `claude` or `claude --continue`, so the head, every orchestrator and
every agent ran on the profile's default model at default effort, and none had a name another
session could address. Now every Claude launch passes `--name`, and `--model`/`--effort` when the
role's setting isn't `inherit`.

- **Names** (`Shared/SessionNames`, pure, public): `fleet-head`, `<project>-main`,
  `<project>-sub-<slug>`, `<project>-<repo>-<branch>`. `SessionNames.Part` keeps ASCII letters,
  digits and `_`; every other character (`/`, `\`, `.`, space, non-ASCII) becomes one `-`, runs
  collapse, and leading and trailing hyphens go. That is the set Claude's @-mention takes
  without quoting. An empty part is left out. An orchestrator record is always named as a sub,
  whatever its `Repository`. `SessionNames.ForAgent(project, repository, branch, orchestrator)`
  is the one entry point for an `AgentRecord`; the SendMessage transport builds on it.
- **One launch value.** `Shared/Constants/ClaudeLaunch(Name, RoleModel)` renders the flags.
  `AgentHarness.CommandFor` and `OrchestratorCommand` take it as an optional `launch` and put
  it on every shape: bare (`claude --name …`), resumed (`claude --continue --name …`), and
  nvim-hosted, where the flags go through the `ClaudeCode` command (`vim.cmd('ClaudeCode
  --continue --name …')`) in both `NvimStartupClaudeOnly` and `NvimStartupWithClaude`, which
  is now a function of its arguments. The head gets the same through `HeadLaunch.ClaudeArgs`.
- **Resume keeps them.** `AgentHarness.Resumed` no longer matches whole commands. It puts
  `--continue` right after `claude`, or right after `ClaudeCode` in an nvim startup, and leaves
  the rest of the command alone, so the daemon's session restore (`FleetDaemon.Relaunch`)
  brings a pane back with the name and model it was started with. A command that already
  continues is returned unchanged. `claude --continue --name X` renames the resumed session
  rather than refusing: checked with Claude Code 2.1.291 (`-p`, haiku), where the transcript
  kept its session ID and gained a second `custom-title` record.
- **Settings.** Model and effort per role. The model is `inherit` or an alias or ID (ASCII
  letters, digits and `-_.:/@[]`, starting with a letter or digit, so it is safe inside
  the nvim Lua string); the effort is `inherit` or `low|medium|high|xhigh|max`
  (`ModelChoice`). Per project, in `settings/<project>.json` as `mainModel`/`mainEffort`,
  `subModel`/`subEffort`, `agentModel`/`agentEffort` (`SettingsConfig.Models`, a
  `RoleModels`), written only when they differ from the default, like
  `mainOrchestratorInNvim`. Defaults: sub-orchestrators `sonnet` at `medium` (they mostly
  route and summarise), everything else `inherit`. A sub can be put back on the profile
  default with `"subModel": "inherit"`. A value that can't be read falls back to the role's
  default, not to `inherit`. The head is global, so its model lives in its own file,
  `<config>/head.json` (`model`, `effort`; `JsonSettingsStore.LoadHead`/`SaveHead`), not in a
  project file and not under a reserved project name.
- **When it applies.** Handlers read the settings when they spawn (`NewAgentHandler` through
  a per-project `Func`, the others when they're built), so a change applies to the next pane.
  A running pane keeps what it was started with, and so does a pane the daemon restores
  from its saved command.
- **No settings UI yet.** feat/menu-reorganization rebuilds the settings menus and wasn't in
  main when this was built, so for now these values live in the settings file only. Rows
  under Fleet config come in a follow-up.

### Rejected

- **Slashes in names** (`<project>/<repo>/<branch>`, as the orchestration research put it).
  The @-mention needs quoting for `/`, and the name is meant to be typed and addressed.
- **Making `Part` lower-case.** The name is shown in the prompt bar and `/resume`, and the
  inputs (project, repo, branch) are already the user's own spelling.
- **Per-role settings as six positional `SettingsConfig` parameters.** One `Models` init
  property, like `StatusHooks`, leaves every existing `new SettingsConfig(...)` alone.

### Not verified

- `--name` with `--continue` in an *interactive* session (only `-p` was tried). The docs
  also say Claude applies "a variant" of the name when another live session on the machine
  already has it, so two fleets on one machine opening the same project would get different
  names. The SendMessage work should check what that variant looks like.
- Names can collide across projects: project `a-b` with repo `c` and project `a` with repo
  `b-c` both give `a-b-c-<branch>`.
- That claudecode.nvim passes `--model`/`--effort` through to `claude` the way it already
  passes `--continue`.
- `--effort` levels a model doesn't support: the docs say "available levels depend on the
  model"; what Claude does with an unsupported one wasn't tried.

## Cross-session messaging spike, 2026-10-06

Question: can `tell_agent`, the head's `relay` and `tell` (and dispatch kickoffs) go
through Claude Code's cross-session messaging (`SendMessage` / `ListAgents`, one inbox per
session, a named pipe on Windows) instead of `send-text`, the screen-scraped ready check
and the head's in-memory queue? Spiked on Claude Code 2.1.291, native Windows, with real
interactive `claude --name spike-recv-*` sessions in a scratch folder. Each ran in a
pseudo-terminal so its screen could be read and keys typed, and had a logging hook on
every event. A fleet agent (Claude inside nvim) did the sending.

- **(a) A delivered message fires `UserPromptSubmit`.** Every delivered message ran
  the receiver's `UserPromptSubmit` hook, whether it started a turn on an idle session
  or was injected into a busy one. The hook's `prompt` is the message wrapped as
  `<cross-session-message from="uds:\\.\pipe\LOCAL\cc-msg-…" from-name="<sender name>"
  from-mode="prompting">\n<text>\n</cross-session-message>`. `Stop` follows as for a
  typed prompt, and so does `PreToolUse`/`PostToolUse` for the tools it causes. No
  other event marks it. A background task finishing (`<task-notification>…`) fires
  `UserPromptSubmit` the same way. So fleet's status hook sees an ordinary turn, which is
  correct. The dispatch hook (`HookPrompt.Intercepted`) only takes a prompt that starts
  with the trigger, and the wrapper starts with `<`. That is safe for every trigger
  except `<` itself, which `DispatchTrigger.IsValid` allows. With that trigger, every
  relayed message would be dispatched.
- **(b) Inbound settings and modes.** Between two prompting sessions (default, auto,
  `acceptEdits`), messages are delivered with no setting at all: default mode to default
  mode, and auto (this agent) to default and back. `crossSessionInbound` in a folder's
  `.claude/settings.local.json` is read: `"hold"` there held a message, and the
  receiver printed *"This repository's settings set crossSessionInbound to hold (a repo
  may only tighten, so your own accept cannot override it)"*. By that rule a repo
  `accept` cannot loosen anything. `/status` shows `Peer address` and `Setting sources`
  but not the inbound value. Not tested: a receiver that bypasses permissions (which
  holds by default). Launching one was refused by auto mode. Fleet starts every pane
  without a permission-mode flag, so its sessions are prompting, and writing
  `crossSessionInbound: "accept"` is neither needed nor (from a repo file) effective.
  It is not written.
- **(c) Delivery timing.** Idle: the message starts a turn within about a second.
  Busy: a message sent during a 30 s foreground `ping` waited until that tool returned
  (22 s later) and was injected at the tool boundary, in the same turn, before the next
  tool call. Nothing was interrupted or lost. This replaces the ready check and the queue.
- **(d) A non-Claude process posting into an inbox.** Hooks do get
  `CLAUDE_CODE_MESSAGING_SOCKET` and `CLAUDE_CODE_MESSAGING_TOKEN` (present from
  `SessionStart` on), so `fleet hook` could publish both. Not pursued, for three reasons:
  - On native Windows the token is how Claude Code recognises an *own-child* message.
    Own-child messages skip the inbound defaults. A post from fleet with that token
    would pass fleet's text off as the session's own Bash/hook output, which defeats
    the trust rules instead of keeping them.
  - Writing the token to a status file publishes a credential.
  - Only the auth line is documented. The message line after it is not, and reading it
    out of the binary was refused by auto mode.

  The *address* is another matter. It is the same value as `/status`'s `Peer address`,
  `SendMessage` accepts it as `to` (verified), and it is not a secret. So hooks can
  safely report it. Hooks also get the session's name as `session_title`
  (`UserPromptSubmit`, `SessionStart`).
- **(e) Claude inside nvim.** It binds an inbox. The research sub-orchestrator
  (`inNvim: true`) and this agent (nvim harness) both appear in `ListAgents` as
  `interactive`, and both sent and received messages.

**Consequence for the design.** Option A: the calling Claude sends. The MCP tool looks up
the target's inbox address (reported by the target's own hook) and answers "send this
with `SendMessage` to `<address>`", and the caller's `SendMessage` carries Claude's own
trust rules (sender name and mode, held/refused notices). The head is a Claude too, so
`relay` and `tell` can work the same way. Option B (fleet writes into the pipe) is
rejected for the reasons under (d). Kickoffs don't fit either option, because the sub's
session doesn't exist yet when `dispatch` returns. They stay on the nvim pump and the
ready marker. A target with no known inbox (older Claude, `--bare`, a pane whose hook
never reported) falls back to `send-text`.

### Built: option A

- **The hook records the address, never the token.** `fleet hook` passes
  `CLAUDE_CODE_MESSAGING_SOCKET` to `HookIo.Event`. The socket becomes the peer address
  (`uds:` + socket) on `HookEvent.Inbox`, then on `AgentReport.Inbox`, and is stored in the
  session's status file (`AgentStateFile.Inbox`). Every event carries it, so the newest
  report has it. `SessionEnd` deletes the file, and `SessionStart` deletes the folder's other
  sessions, so a closed session's address goes with it.
- **A port for the lookup.** `Ports/Agents/IAgentInboxes.AddressAsync(folder)`, implemented by
  `Platform/Storage/StatusFileInboxes` over the status store, picks the newest report with an
  inbox through `AgentStatusRules.InboxOf`. It matches the **exact** folder, not `Within`.
  The project root is the main orchestrator's folder, and agent worktrees can sit under it.
- **`tell_agent`.** `TellAgentHandler.RouteAsync` returns the address when the agent's
  folder has one. `McpActions` then answers with `PeerMessage.SendYourself` (the address,
  the message unchanged, and "a held or refused notice is the receiver's choice"), and
  nothing is typed or written to `instruction.md`. Without an address it is `DeliverAsync`,
  as before. A new optional `typed` argument forces typing. The result offers it only for a
  `SendMessage` that couldn't reach the address (for example a crashed Claude whose pane
  survived). A held message is not a reason to use it: typing would get round the
  receiver's hold.
- **The head's `relay` and `tell`.** `HeadDeps.Inboxes` is set only for the local head
  (`HeadWiring.RunMcpAsync`). The remote side (`ServeOrigin`) leaves it null, because an
  address there names a pipe on the other machine. An open project with an address for
  its root, and nothing in the typed queue for it, gets the address back without the screen
  being read, as long as the main orchestrator's pane is listed. A project the head had to
  open is typed into as before. Its status folder may still hold the address of a session
  that was killed without `SessionEnd`, and that address is only replaced when the new
  session's `SessionStart` lands, which can be after the pane looks ready. The head has no
  `typed` retry, so it doesn't take that risk. `tell` still asks through X's `tell_agent` rule. A relay becomes
  `PeerMessage.DispatchRequest`, which asks the orchestrator to call its `dispatch` tool,
  because a wrapped message can't trip the hook. So the head checks only that the `dispatch`
  rule doesn't forbid it, and leaves the *ask* to the orchestrator's own `dispatch` call,
  which `McpGate` checks against the same rule. Asking in both places would show two
  dialogs for one relay.
- **The dispatch hook ignores Claude's own wrappers.** `HookPrompt.Intercepted` never takes
  a prompt that starts with `<cross-session-message` or `<task-notification>`
  (`PeerMessage.FromClaude`), whatever the trigger. This change is limited to the hook path.
- **Briefs.** The head's brief, the head tool descriptions, the `tell_agent` description and
  `OrchestrationText.DefaultHowYouWork` say to send the returned message with
  `SendMessage` to the returned address.
- **Not written:** `crossSessionInbound`. Fleet's sessions are prompting, and a repo file can
  only tighten (see (b)).
- **Kept:** send-text, the ready check and the queue, as the fallback; kickoffs through
  `instruction.md`.

Known limits, from review:

- The address is the newest one reported for the folder, whichever session sent it. A
  second `claude` started by hand in the same folder outside fleet would take the messages
  meant for fleet's pane.
- `typed: true` is a request, not an enforced fallback. A caller that passes it after a
  *held* delivery types round the receiver's hold. Only the tool text says not to.
- For `tell_agent`, a Claude that crashed while its pane lived on keeps its old address
  until its folder starts a new session. The `typed` retry covers that case.

Not verified end to end: a real head relaying to a real orchestrator through
`SendMessage`, or an orchestrator acting on a dispatch request from the head. The unit
tests cover the routing, and the spike covers delivery.

## Dispatch without a sub-orchestrator, 2026-10-06

Every dispatch used to start a sub-orchestrator: a full Claude session in its own pane whose
only job, for a one-repo task, was to call `new_agent` and relay. The orchestration research
(`fleet-orchestration-research`, §3 and §5 item 4) called that the expensive tier. Now the
dispatcher can skip it, but only when it says so.

- **Explicit, never guessed.** The `dispatch` MCP tool takes an optional `repository`, an
  optional `branch` and an optional `research` flag. With a repository, fleet starts a repo
  agent with the task directly, through the same code as `new_agent` with `task` (`McpActions`
  implements the new `IAgentStarter` port, so the Orchestrations slice doesn't reach into the
  Agents slice). The owner is the caller, as with `new_agent`: none when the main orchestrator
  dispatches, so it shows as a top-level agent, and the sub when a sub dispatches, so it stays in
  that sub's tree. It gets the repo-agent session name and the `agent` model/effort from #49, because it is spawned
  by `NewAgentHandler`. Without a branch, the branch is named from the task the way a sub's
  slug is (the slug namer, then `OrchestrationSlug`), made unique among that repository's
  agents and its branches (local and `origin/`, through `IAgentStarter.BranchesAsync`), so a
  derived name never lands on an old branch that `git worktree add` would reuse. Without a repository nothing changes. The reply's `Slug` is the branch and its
  `Folder` is empty.
- **One pure rule** (`Shared/Orchestrations/DispatchRouting.Decide`): resolve AIDLC as before
  (the prefix, then with mode `on` the argument, then the project default; this moved out of
  `DispatchHandler` unchanged); research is the `research` flag or the `research` profile; a
  repo agent is started only when a repository is given, AIDLC doesn't apply and it isn't
  research. Otherwise it is a sub-orchestrator, so the AIDLC record and gates keep living in
  its orchestration folder, and the reply says the repository was not used and why. With AIDLC off a `feature:` prefix is ordinary text, so it stays in
  the direct agent's task.
- **Research.** A research sub gets a `## Research` section after the process:
  `OrchestrationText.Research`, its own constant, telling it not to start repo agents and to use
  background subagents or `/deep-research`. `DefaultHowYouWork` is untouched, because the
  subagent-guidance branch reworks it next.
- **Permissions.** A `dispatch` call that names a repository is gated as `dispatch` and then
  `new_agent` (`McpGate.Gated`; `McpDispatcher` runs the same forbid/ask check for each, in that
  order, and stops at the first refusal). A project that forbids `new_agent` can't start an
  agent through `dispatch`. It is gated on the arguments, not on the route, so with AIDLC on
  (where a sub is started after all) `new_agent` is still checked: the gate can't know the
  route without loading AIDLC settings, and erring towards one more check is the safe side.
  The dispatch approval text now shows `<repository>: <task>` instead of the repository alone.
- **Typed trigger and `fleet dispatch` unchanged.** They always start a sub-orchestrator.
  `DispatchHandler` without a starter refuses a repository instead of ignoring it.

### Rejected

- **Inferring the shape from the prompt** (one repository named in the prose means direct).
  Wrong guesses would bypass the sub silently; the task says explicit.
- **A typed form for the hook path**, such as `,repo:backend fix …` or `,backend/fix-x: …`.
  The `word:` prefix already means an AIDLC profile, so a repository named like a profile, or
  a sentence that starts with a repository name and a colon, would be read the wrong way; and a
  typed dispatch has no place for the branch. The main orchestrator's Claude can call the tool
  with a repository when that's what the user asked for.
- **Gating only on the route** (check `new_agent` only when a repo agent is really started).
  The route depends on the project's AIDLC settings, which the gate would have to resolve a
  second time; the extra check is harmless.
- **Calling `NewAgentHandler` from `DispatchHandler`.** A slice may not reference another slice.

### Not verified

- An end-to-end direct dispatch against a real Claude and WezTerm. The direct path in
  `McpActions` (repository lookup, `ClaudeWiring.ApproveFolder`, the ready wait before the task
  is typed) is the `new_agent` code moved into a method, and `McpActions` has no tests.
- The task is typed into the new agent the way `new_agent` does it, waiting up to 24 s for the
  ready marker while the MCP call (and the dispatcher's one-call-at-a-time gate) is held.
- Whether a branch named only from the slug (no `feat/` prefix) suits every repository's
  branch conventions; the dispatcher can pass `branch` when it matters.

## Subagent guidance in the briefs, 2026-10-06

The orchestration research (§2, §3, §5 item 5) found that agents did side work inline, or asked
for another fleet agent, where one of Claude Code's own subagents would do it for the cost of a
context window. Fleet now says so in two places, and the text lives in its own constants
(`Shared/Orchestrations/SubagentGuidance`), so `DefaultHowYouWork` and the brief text other
branches edit stay untouched.

- **Sub-orchestrators** get a `## Subagents` section in their CLAUDE.md, after the process and
  research sections and before `## How you work` (`OrchestrationText.Instructions(..., subagents)`):
  read, check and look things up with a subagent (Explore, or a general-purpose one told not to
  edit) instead of starting a fleet agent; a fleet agent is for a change that needs its own branch.
  It is its own section, so a project's how-you-work override doesn't drop it. It tells the sub
  that fleet already passes the repo-agent guidance on, so subs don't have to remember it.
- **Repo agents** are launched with `--append-system-prompt-file .fleet/subagent-guidance.md`.
  `NewAgentHandler` writes the file into the worktree before it spawns the pane, when the agent
  runs Claude and the project's setting is on, and removes it when the setting is off. Every agent
  that runs Claude is started there: `new_agent`, a direct dispatch (#54, through `IAgentStarter`
  and the same handler) and the dashboard. The guidance: delegate searches, logs and test runs
  whose output you won't need verbatim to a subagent (Explore for code search); run a read-only
  review subagent on the diff before opening a PR; use `isolation: worktree` subagents for
  throwaway parallel attempts instead of asking for more fleet agents.
- **Restarts keep what the agent was created with.** `ClaudeLaunch.ForAgent` takes the worktree
  and adds the flag when the file is there, so `OpenAgentHandler` and `RestoreSessionHandler`
  launch a resumed agent the same way, without reading settings. Claude Code records the system
  prompt on a conversation's first request and reuses it on `--continue` until it compacts, so
  deciding at creation is what actually happens anyway.
- **Built-in subagents only.** The text names Explore and general-purpose, never the user's
  `quick`, `worker`, `deep` or `reviewer`, which live in one profile and aren't on every machine
  (a test checks this). The review subagent is "a general-purpose subagent told to read the diff
  and not edit", not a definition fleet ships.
- **Setting.** `SubagentGuidance` per project, default on, stored only when off
  (`"subagentGuidance": false` in the project's settings file), like `statusHooks`. No UI. Off
  removes both the sub-orchestrator section and the repo-agent file.
- **Cost.** About 100 tokens in each sub-orchestrator's CLAUDE.md and about 150 in each repo
  agent's system prompt, paid once per session and cached with the rest of the system prompt.

### Rejected

- **Shipping a `fleet-reviewer` through `--agents` JSON.** The JSON would have to survive the
  WezTerm argv, a single-quoted Lua string in `NvimBoot` and the ClaudeCode plugin's own argument
  splitting; the file form of `--agents` is `--print` only. Each defined agent also adds its
  description to every session's Agent tool listing. The built-in general-purpose subagent with
  a one-line instruction does the same review.
- **A fleet section in the worktree's `.claude/`** (an agent definition or rules file). `.claude/`
  is git-excluded in fleet worktrees, but it is the user's repo configuration and a checked-in
  `.claude/` would be shadowed or mixed with fleet files.
- **Appending the text to the kickoff (`.fleet/instruction.md`).** It reaches only agents that
  get a task, and a `tell_agent` overwrites the file; the system prompt reaches every session.
- **Inline `--append-system-prompt "<text>"`.** Multi-line text with spaces doesn't survive the
  nvim path's quoting; a relative path with no spaces does.
- **Re-reading the setting on every restart.** The recorded system prompt wins on `--continue`
  anyway (see above), and it would mean passing settings into every reopen path.

### Not verified

- That the ClaudeCode nvim plugin passes `--append-system-prompt-file .fleet/subagent-guidance.md`
  through to `claude` unchanged. The test only checks the `:ClaudeCode` command line fleet builds.
- `--append-system-prompt-file` in an interactive session. The CLI reference says the system
  prompt flags "work in both interactive and non-interactive modes"; not run on a real machine.
- That a resumed session whose worktree lost `.fleet/subagent-guidance.md` behaves: fleet then
  launches it without the flag, so it can't fail on a missing file, but the recorded prompt still
  has the old text.
- Whether agents follow the guidance in practice (fewer inline test logs, a review before the PR).
  Check a few transcripts with `/usage` attribution.

## Still to verify
## Still to verify
- Whether Tomlyn is AOT-clean, or whether harness config should be JSON with a
  source-generated context.

## Parked

- `DeBlasis.GhosttyVt` — a Ghostty VT binding on NuGet. Irrelevant now, but it
  is the component tiling would need if the `embedded` driver ever grows past
  fullscreen attach.

## Notes

- `C:\repos\fleet` is not yet a git repository, so this document is uncommitted.
- Neovim config lives at `%LOCALAPPDATA%\nvim`, is itself a git repo, and has a
  `bootstrap.sh` — portability there needs confirming, not assuming.
