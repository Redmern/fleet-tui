# Run a repository — design

**Status:** approved in brainstorming, revised after spec review.
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

Profiles live per project in `<fleet config>/runs/<project>.json`: an object
`{ "version": 1, "profiles": [ … ] }`, matching how the session store wraps its
array. `FleetPaths.Runs` names the folder and `EnsureDirs` creates it. A
repository without a profile has no run profile; nothing is inferred.

The URL of a running repository is `http://localhost:<port><path>`.

### The run pane and who owns it

A run pane is a tab titled `FleetTabTitles.Run(repository)` = `<repository> run`.
Its working directory is the repository's default-branch worktree, which is also
the directory Enter opens and may be an agent's worktree. Title, not cwd, is what
identifies it, and two existing matchers must learn to step around it:

- `OpenRepositoryHandler` finds an already-open repository pane by cwd. It must
  skip panes whose title `FleetTabTitles.IsRun(title)`, or Enter would focus the
  dev server and never open the editor.
- `AgentPaneMatch.Owns` claims panes by cwd too. It must return false for run
  titles, or an agent living in that worktree would move or kill the server on
  hide, stop and rename. `IsRun` is a suffix check on ` run`; agent titles are
  `repo/branch-slug` and never end that way.

Both live outside the new slice, so the title helpers sit in
`Shared/Constants/FleetTabTitles` next to `Dashboard`.

### Starting

Starting a repository, given `project`, `projectRoot`, the repository choice and
its profile:

1. No profile: from the dashboard, prompt for command, port and path (path
   prefilled `/`), save, continue; cancel does nothing. From MCP, fail: "no run
   command for `<repo>`; set one first."
2. Resolve the working directory with `RepositoryWorktree.For(directory,
   defaultBranch, Directory.Exists)`. Missing: fail "`<dir>` is gone."
3. Refuse when a run pane for this repository exists ("`<repo>` is already
   running at `<url>`") or when the port is bound on localhost ("port `<n>` is
   in use").
4. Find the project window the way `OpenRepositoryHandler` does: the window of a
   pane whose cwd is `projectRoot`. None found (an MCP call with no dashboard
   open): spawn a new window, as `OpenAgentHandler` does.
5. Spawn with `Cwd` = the worktree, `SessionName` = project, `Args` = the shell
   wrapper around the command, then title the tab. Same spawn path agents use,
   so any mux driver works.

### Stopping

Kills every pane whose title is the run title. None: fail "`<repo>` is not
running." Killing the pane ends the shell and its child; that is the only
process control.

### Status

`RunStatus.For(repository, profile, panes)` is pure and returns
`(bool Running, string? Url)`: `Running` is "a run pane exists for this
repository", `Url` is the profile's URL or null when there is no profile. Both
the Repositories tab and `run_status` call it. Nothing is stored about running
state.

### Rename, remove, and re-setting

- Rename and remove refuse while the repository is running, with the same
  wording style as the existing "still has agents" guard.
- Remove deletes the profile. Rename re-keys the profile to the new name.
- "Set the run command" refuses while running ("stop `<repo>` first"), since a
  changed port would advertise a URL the live server is not on. Empty command
  deletes the profile.

### Surfaces

**Repositories tab.** A running repository's row gets a `:5173` pill after its
branch pill, muted like the repository name. A run pane with no profile shows a
bare `run` pill. Enter keeps opening the repository; running goes through the
manage picker.

**Manage picker.** `RepositoryChores.Entries(bool running)` keeps the five
existing entries and constants and adds two stable constants: `Run` = 5, whose
label is "Run the application" or "Stop the application" by `running`, and
`SetRun` = 6, "Set the run command". The prompt asks command, port and path,
prefilled from the current profile.

**MCP tools** for agents, all taking `repository`:

| Tool | Extra arguments | Default policy |
|---|---|---|
| `run_repository` | — | allow |
| `stop_run` | — | allow |
| `run_status` | — | allow |
| `set_run_command` | `command` (string, required), `port` (integer, required), `path` (string, optional) | ask |

`run_status` answers with running yes/no and the URL, or "no run profile". New
tools go everywhere a `HarnessTool` goes: `HarnessToolIds.For`/`Parse`,
`SettingsDefaults` (the Permissions-screen order, the allowed-by-default set, the
label map), `McpTools.All`, `McpActions`, `ToolArguments` (`command`, `port`,
`path`). They then appear on the Permissions screen and sync to Claude's config
like every other tool.

## Design

### Units

| Unit | Location | Does | Depends on |
|---|---|---|---|
| `RunProfile` | Ports/Runs/Models | The record above, plus `Url`. | — |
| `IRunStore` | Ports/Runs | `IReadOnlyList<RunProfile> List(project)`, `Save(project, profile)`, `Remove(project, repository)`. | `RunProfile` |
| `JsonRunStore` | Platform/Storage | File-backed `IRunStore`; `RunsFile` DTO in `FleetJsonContext`; lock-guarded atomic replace like the session store. | `FleetPaths` |
| `FleetTabTitles.Run`, `IsRun` | Shared/Constants | Run tab title and its test. | — |
| `ShellLine.Command(bool windows, string line)` | Shared | `["cmd","/c",line]` or `["sh","-c",line]`. `EnvLaunch.Wrap` adopts it so the rule lives once. | — |
| `RunPanes.Owns(pane, repository)` | Features/Repositories (area root) | Title match. | `FleetTabTitles` |
| `RunStatus.For(repository, profile, panes)` | Features/Repositories (area root) | Pure status, see above. | `RunPanes` |
| `RunRepositoryHandler` | Features/Repositories/RunRepository | Steps 1–5. `Result<string>` with the URL. | `IMuxDriver`, `IRunStore`, `Func<int,bool>` port probe, `bool windows` |
| `StopRunHandler` | same slice | Kills owned panes. | `IMuxDriver` |
| `SetRunCommandHandler` | same slice | Validates; saves or removes; refuses while running. | `IRunStore`, panes |
| `PortProbe.InUse(port)` | same slice | `IPGlobalProperties.GetActiveTcpListeners()`; the handler takes it as `Func<int,bool>` so tests never touch the network. | .NET |
| `RunPrompt` | same slice | Dashboard form: command, port, path → `RunProfile`, built like `AddRepositoryView`. | Ui |
| Composition | Cli/Composition | Wires handlers, evaluates `RunStatus` per row, passes `OperatingSystem.IsWindows()`. | all above |

One slice holds three handlers. That departs from the one-behaviour-per-folder
habit (`StopAgent`, `RemoveAgent` are separate) because the three share the
profile and pane rules and nothing else uses them; stated here so nobody splits
it by reflex later.

### Row data flow

`DashboardRows.ForRepositories` sits in the `Dashboard/ShowDashboard` slice and
cannot call `RunStatus`. It gains a `Func<RepositoryChoice, string?> runPill`
argument; the composition root evaluates `RunStatus` against the panes it
already lists for the bar state and returns `:5173`, `run`, or null.

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
| Set while running | `SetRunCommandHandler` | Fail: "stop `<repo>` first." |
| Rename or remove while running | existing handlers' wiring | Fail: "`<repo>` is running; stop it first." |

Every result is logged through `Noted` like the other repository actions.

### Concurrency

A dashboard and several agents may act at once. The run store follows the
agent store's rule: lock-guarded, atomic replace. Two concurrent starts race on
the pane check; the loser's spawn creates a second pane. Acceptable for v1:
"Stop" kills all owned panes, and the port check catches most of it.

### Assumptions

- The mux domain is the local OS. Over an ssh domain the shell wrapper would be
  the remote's; not handled.
- wezterm's default `exit_behavior` closes a pane when its process exits. A
  config that holds dead panes open would report a dead server as running.

## Testing

- `RunStatusTests`: no profile and no pane; profile, not running; running with
  URL; pane without profile.
- `RunRepositoryTests`: spawns in the worktree with the shell wrapper, in the
  project window, titled; new window when none; refuses when running, when the
  port is bound, when no profile, when the worktree is gone.
- `StopRunTests`: kills all owned panes; fails when none.
- `SetRunCommandTests`: validation table; empty command removes; refuses while
  running.
- `JsonRunStoreTests`: round trip, missing file, atomic overwrite.
- `OpenRepositoryTests`: a run pane in the worktree is not "already open".
- `AgentPanesTests`: a run-titled pane is never an agent's.
- `RepositoryChoresTests`: seven entries, label follows `running`, constants stable.
- `DashboardRowsTests`: pill from `runPill`, absent when null.
- `McpToolsTests` / `HarnessToolIdsTests`: specs and id round trips.
- Architecture tests already enforce slice boundaries and no comments.

## Verify before coding

- `IPGlobalProperties.GetActiveTcpListeners()` under NativeAOT on both OSes.
- A command line with quotes, e.g. `dotnet run --urls "http://localhost:5000"`,
  survives `wezterm cli spawn -- cmd /c <line>`; cmd's quote stripping is the risk.
- Killing the pane ends the child on Windows (ConPTY close) and under `sh -c`
  with `&&` on Linux, where `sh` stays the parent.

## Open for later specs

The browser core will read `RunStatus` for the URL to open. The viewer pane will
sit beside the run pane. Neither changes anything here.
