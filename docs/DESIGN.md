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
clients, and attaches one pane at a time as fullscreen raw passthrough.
Deliberately no tiling and no terminal emulator — attach/detach and tiling are
separable, and tiling is the expensive half. Tiling forces a VT emulator, and a
VT emulator is what mangles Neovim (truecolor, undercurl, SGR mouse, bracketed
paste, focus events, kitty keyboard protocol).

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
  fleet (client)                 fleetd (daemon)              children
  -------------                  ---------------              --------
  Terminal.Gui dash --socket-->  session registry     --pty-->  nvim
  raw-mode attach   <-stream-->  pane table                     claude
  fleet CLI verbs   --socket-->  PTY owner + ring buf           shell
```

### fleetd

One daemon per user per machine. It owns every PTY and outlives all clients —
that single property is what buys detach and reattach.

Spawning it detached is platform-specific and easy to get subtly wrong: on
Windows, `CreateProcess` with `DETACHED_PROCESS` and no inherited handles; on
Unix, `setsid` with stdio redirected away from the parent's terminal. Double-fork
daemonization is not safe in .NET.

### Transport

`\\.\pipe\fleet` on Windows, `$XDG_RUNTIME_DIR/fleet/<name>.sock` on Linux.
ndjson control frames plus a raw byte stream for attach. Both sides are BCL
types; this is the cheapest part of the driver.

### Panes

Everything is a pane, Neovim included. This is the driver's private
representation; it satisfies the interface's `Pane` but carries more:

```
Pane { Id, Kind: Editor|Agent|Shell, Cwd, Argv, Pty, Ring, Status }
```

Consequence worth wanting: the editor session survives a dropped SSH connection
the same way the agents do.

### Attach

Fullscreen raw passthrough. The client puts its console in raw mode, copies
stdin to the PTY and PTY to stdout verbatim, and forwards resize events. Nothing
in the path parses the stream, so Neovim gets truecolor, SGR mouse, kitty
keyboard, and undercurl intact.

### Repaint on attach — known soft spot

The daemon keeps a per-pane ring buffer of recent output. On attach it replays
the ring, then pokes a resize so full-screen applications redraw themselves.

This is a heuristic, not a screen model. tmux instead runs a headless terminal
emulator per pane and dumps exact screen state on attach. If replay proves
unreliable, that is the upgrade — and it is the same component tiling would
later need, so the work would not be wasted.

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

## Still to verify
- Terminal.Gui v2 AOT on a real **Linux** runner. Windows is now proven; the CI
  matrix answers Linux on first push.
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
