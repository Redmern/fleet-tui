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
    wezterm present AND GUI reachable  -> wezterm  (base)
    tmux present                       -> tmux     (fallback)
    otherwise                          -> embedded (last resort)
```

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

### Storage — deliberately deferred

Two candidates:

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

- **Agent state storage.** Daemonless files versus a state daemon. Deliberately
  deferred behind `IAgentStateStore` until the `wezterm` driver is working; M2 is
  where the evidence arrives.

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

- [ ] **Alt combinations.** Alt+letter, Alt+Shift+letter (must not collapse to
      uppercase), Alt+Backspace, Ctrl+Alt+letter (fish decodes these as both
      modifiers). Right Alt is AltGr only with `LEFT_CTRL` also set.
- [ ] **Ctrl+S** reaches the pane (not taken as XOFF, not claimed by the host;
      WezTerm on this machine claims it as a leader). Also Ctrl+/, Ctrl+1..9 as
      keys rather than control bytes, and Ctrl+J as LF, distinct from Enter.
- [ ] **Dead keys and AltGr text.** US-International `'` + `e` gives `é` once,
      with no extra base character; AltGr+dead key; non-US shifted text such as
      `@` on German layouts; IME commits; emoji from the Windows picker
      (surrogate pairs, or CSI-u with associated text under WezTerm).
- [ ] **SGR mouse past column 95.** Coordinates must use SGR (1006) encoding end
      to end. Legacy encodings stop at 95/223. Reattach must restore mouse
      reporting.
- [ ] **Pasted Enter.** A multi-line paste from Windows Terminal arrives as key
      records with `VK_RETURN`. It must reach the pane as one bracketed paste with
      its newlines, and must not submit each line (herdr: Codex lost Enter after
      long pastes; OMP/Pi submitted per line). LF-only pastes keep their
      newlines.
- [ ] **Mode restore.** On exit and on detach, reset mouse (1000/1002/1003/1006),
      focus (1004), bracketed paste (2004), cursor keys, keypad, cursor shape and
      visibility, and the alt screen. Restore console modes and code pages even
      on a crash (herdr #4055 still had a Git Bash report open).
- [ ] **Escape** is sent at once, not held as a possible Alt prefix, and a lone
      Esc beside another key is not fused into an Alt chord.
- [ ] **Shift+Enter** keeps its modifier. **Shift+Tab** reaches the pane as
      CSI Z; the permission-mode cycle depends on it.
- [ ] **Key repeat and release** stay with the pane that got the press.
- [ ] **Incomplete host replies** (split `ESC ]` OSC colour answers) are not
      mistaken for Alt+`]`.

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
