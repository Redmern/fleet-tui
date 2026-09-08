# Run a repository — design

**Status:** approved in brainstorming, awaiting spec review.
**Date:** 2026-09-08
**Series:** first of three. Later specs cover the browser core (headless Chromium
driven over the DevTools protocol, exposed to agents as MCP tools) and the viewer
pane (screenshots painted into wezterm with key and mouse forwarding). This spec
stands alone and ships alone.

## Goal

Fleet knows how to start a repository's application and where it is served, so a
person can bring it up from the Repositories tab and an agent can bring it up from
its MCP tools. Everything later that "looks at the app" reads the URL from here.

## Non-goals

- Build steps before the run command. The run command is one shell line; a repo
  that needs `npm ci && npm run dev` writes exactly that.
- Running inside an agent's worktree, or one server per branch. A repository runs
  once, from its default-branch worktree.
- Health checks, log scraping, restart on crash, or noticing that the process
  exited. Status is derived from live panes only.
- Anything browser. That is the next spec.

## Behaviour

### Run profile

A repository may carry one run profile:

| Field | Type | Meaning |
|---|---|---|
| `repository` | string | Repository name, as listed on the Repositories tab. |
| `command` | string | One shell line, run as `cmd /c <line>` on Windows and `sh -c <line>` elsewhere. |
| `port` | int | TCP port the server listens on. 1–65535. |
| `path` | string | URL path appended to the host. Default `/`. Must start with `/`. |

Profiles live per project in `<fleet config>/runs/<project>.json`, one file per
project, a JSON array of profiles. A repository without a profile has no run
profile; nothing is inferred.

The URL of a running repository is `http://localhost:<port><path>`.

### Starting

Starting a repository:

1. Loads its profile. No profile: the dashboard prompts for command and port,
   saves the profile, and continues. Over MCP: refuse with "set a run command
   first" (the agent has `set_run_command`).
2. Resolves the working directory with `RepositoryWorktree.For(directory,
   defaultBranch, Directory.Exists)`, the same directory the Repositories tab
   opens with Enter.
3. Refuses when a run pane for this repository already exists ("already running")
   or when the port is already bound on localhost ("port 5173 is in use").
4. Spawns a pane in the project's window with `Cwd` = that directory and
   `Args` = the shell wrapper around the command, then titles its tab
   `<repository> run`. This is the same spawn path agents use, so it works over
   any mux driver.

### Stopping

Kills every pane whose tab title is `<repository> run`. No pane: "not running".
Killing the pane kills the shell and its child; that is the only process control.

### Status

`RunStatus.For(profile, panes)` is pure: it returns whether a run pane exists and,
when a profile exists, the URL. The Repositories tab and the MCP `run_status`
tool both call it. Nothing is stored about running state.

### Surfaces

**Repositories tab.** A running repository's row gets a `:5173` pill after its
branch pill, muted like the repository name. Enter keeps opening the repository;
running goes through the manage picker.

**Manage picker** (the existing `m` chores list) gains, after "Rename":

- "Run the application" when not running; "Stop the application" when running.
- "Set the run command" — prompts for command, port and path, prefilled from the
  current profile; empty command deletes the profile.

**MCP tools** for agents, all taking `repository`:

| Tool | Extra arguments | Default policy |
|---|---|---|
| `run_repository` | — | allow |
| `stop_run` | — | allow |
| `run_status` | — | allow |
| `set_run_command` | `command` (string, required), `port` (integer, required), `path` (string, optional) | ask |

`run_status` answers with running yes/no and the URL, or "no run profile".
Policies follow the existing settings model: they appear on the Permissions
screen and sync to Claude's config like every other tool.

## Design

### Units

| Unit | Layer | Does | Depends on |
|---|---|---|---|
| `RunProfile` | Ports/Runs/Models | The record above. | — |
| `IRunStore` | Ports/Runs | `IReadOnlyList<RunProfile> List(project)`, `Save(project, profile)`, `Remove(project, repository)`. | `RunProfile` |
| `JsonRunStore` | Platform/Storage | File-backed `IRunStore`, source-generated JSON, atomic write like the other stores. | `FleetPaths` |
| `ShellLine` | Shared | `Command(bool windows, string line)` → `["cmd","/c",line]` or `["sh","-c",line]`. | — |
| `RunPanes` | Features/Repositories/RunRepository | `Title(repository)` = `<repository> run`; `Owns(pane, repository)` by tab title. | `Pane` |
| `RunStatus` | Features/Repositories/RunRepository | Pure: `For(profile, panes)` → `(bool Running, string? Url)`. | `RunPanes` |
| `PortProbe` | Features/Repositories/RunRepository | `InUse(port)` via `IPGlobalProperties.GetActiveTcpListeners()`; injectable `Func<int,bool>` for tests. | .NET |
| `RunRepositoryHandler` | same slice | Steps 1–4 above. Returns `Result<string>` with the URL. | `IMuxDriver`, `IRunStore`, `PortProbe` |
| `StopRunHandler` | same slice | Kills owned panes. | `IMuxDriver` |
| `SetRunCommandHandler` | same slice | Validates and saves or removes a profile. | `IRunStore` |
| `RunPrompt` | same slice | Dashboard form: command, port, path → `RunProfile`. Uses `FleetPrompt`/`FleetRows` like `AddRepositoryView`. | Ui |
| MCP wiring | Features/Mcp, Shared/Settings, Cli/Composition | Four `HarnessTool` values, ids, specs, default rules, `McpActions` cases. | handlers |
| Dashboard wiring | Cli/Composition, Features/Repositories | Chore entries, `ManageRepository` cases, row pill. | handlers, `RunStatus` |

Each handler takes what it needs in its constructor and is tested against
`FakeMuxDriver` and an in-memory `IRunStore`, the same way agent handlers are.

### Validation (in `SetRunCommandHandler`)

- `command` trimmed, non-empty unless deleting.
- `port` in 1–65535.
- `path` defaults to `/`, must start with `/`, no whitespace.
- `repository` must exist in the project; the wiring passes the current list.

### Error handling

| Situation | Where | Result |
|---|---|---|
| No profile, from dashboard | wiring | Prompt, save, continue. Cancel → nothing. |
| No profile, from MCP | `RunRepositoryHandler` | Fail: "no run command for `<repo>`; set one first." |
| Already running | handler | Fail: "`<repo>` is already running at `<url>`." |
| Port bound | handler | Fail: "port `<n>` is in use." |
| Worktree missing | handler | Fail: "`<dir>` is gone." |
| Mux did not respond | handler | Fail with the existing "multiplexer did not respond" text. |
| Stop with no pane | `StopRunHandler` | Fail: "`<repo>` is not running." |

Every result is logged through `Noted` like the other repository actions.

### Concurrency

A dashboard and several agents may act at once. The run store follows the
agent store's rule: lock-guarded, atomic replace. Two concurrent starts race on
the pane check; the loser's spawn creates a second pane. Acceptable for v1:
"Stop" kills all owned panes, and the port check catches most of it.

## Testing

- `RunStatusTests`: pure cases — no profile, profile not running, running.
- `RunRepositoryTests`: spawns in the worktree with the shell wrapper and titles
  the tab; refuses when running, when the port is bound, when no profile.
- `StopRunTests`: kills all owned panes; fails when none.
- `SetRunCommandTests`: validation table; empty command removes.
- `JsonRunStoreTests`: round trip, missing file, atomic overwrite.
- `RepositoryChoresTests`: new entries and their order.
- `DashboardRowsTests`: pill appears only when running.
- `McpToolsTests` / `HarnessToolIdsTests`: specs and id round trips.
- Architecture tests already enforce slice boundaries and no comments; the new
  slice is `Features/Repositories/RunRepository`.

## Verify before coding

- `IPGlobalProperties.GetActiveTcpListeners()` under NativeAOT on both OSes.
- `sh -c` is present on the Linux targets the installer supports (it is; POSIX).

## Open for later specs

The browser core will read `RunStatus` for the URL to open. The viewer pane will
sit beside the run pane. Neither changes anything here.
