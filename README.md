# fleet

A TUI orchestration tool for AI coding work across projects that span one or more
repositories. Neovim is the editor, Claude Code is the agent, and a terminal
multiplexer supplies the panes.

Opening a project gives you one window split in two: Claude on the left, the
fleet dashboard on the right. A prefix chord — `ctrl+space` by default — opens
the fleet menu from **any** pane, including one running nothing but Claude.

> **Status.** Projects, repositories, the menu, configurable keybinds and agents
> all work: you can start an agent in its own worktree, restart it after closing
> the terminal, change what it opens, hide it from the tab bar, stop it, and
> remove it together with its worktree. Live agent status via hooks is not built
> yet. Only the WezTerm driver ships; tmux and the embedded driver are designed
> but unwritten. Nothing has been built or run on Linux.

## Requirements

- Windows, with [WezTerm](https://wezterm.org) on `PATH`
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- git
- Visual Studio Build Tools 2022 with the **Desktop development with C++**
  workload — NativeAOT needs the MSVC linker and the Windows SDK
- Claude Code, for the pane fleet opens on the left

## Install

```powershell
.\install.ps1
```

This publishes a NativeAOT binary, copies it to
`%LOCALAPPDATA%\Programs\fleet\fleet.exe`, adds that directory to your user
`PATH`, and runs `fleet doctor` to verify. Open a new terminal afterwards.

`.\install.ps1 -Uninstall` reverses it. Add `-Purge` to delete `%APPDATA%\fleet`
as well.

If publishing fails with `'vswhere.exe' is not recognized` followed by `MSB3073`,
the toolchain is fine and only `vswhere` is missing from `PATH`; the installer
adds it automatically, but a manual `dotnet publish` needs:

```powershell
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"
```

## Commands

```
fleet                       pick a project and open it
fleet dash --project <name> the dashboard (runs inside a pane)
fleet menu                  the fleet menu, or the picker outside a project
fleet menu --action <id>    jump straight to add-repository or keybinds
fleet request --action <id> --project <name>
                            hand an action to that project's running dashboard
fleet apply-keybinds        write the wezterm keybinding module
fleet doctor                check the environment
```

`fleet doctor` is the end-to-end smoke test: it reports the config directory, the
selected multiplexer driver and whether it responds, the git version, every saved
project, and any failures fleet swallowed.

fleet works from any terminal, not just a wezterm pane. Inside a pane it talks to
the mux named by `WEZTERM_UNIX_SOCKET`; outside one it finds a live wezterm socket
itself. If several wezterm windows are running as separate GUI processes, it targets
the most recently used one that answers.

## The menu

```powershell
fleet apply-keybinds
```

writes `~/.wezterm/fleet.lua` from your current keymap. Add to `.wezterm.lua`:

```lua
local fleet = require 'fleet'
fleet.apply(config)
```

`ctrl+space` then opens **fleet's own menu** — drawn by fleet, styled like the rest
of it, one key per entry. It opens as a tab, so no pane is resized, and closes
itself when done.

```
╭┤ fleet menu ├──────────────────────╮
│ Quit fleet            Q            │
│ Keybinds              k            │
│ Go to the main pane   m            │
│ List agents           l            │
```

- **Quit fleet** (`Q`, deliberately shifted) closes every pane of the project, including hidden agents in
  their own workspace, so nothing is left running invisibly. Agent records are
  already on disk, so reopening the project lists them again.
- **keybinds** opens the editor in the dashboard and **focuses that pane**, so you
  end up looking at the view you asked for even from an agent pane.
- **main pane** jumps to the dashboard. This one never starts a fleet process.
- **List agents** opens a list with **Open** and **Hidden** tabs, switched with
  `h`/`l` or the arrows; `enter` goes to the agent.

The dashboard keys act on whatever row is selected there, so they are not in this
menu — a menu you can open from a claude pane cannot act on a selection you cannot
see.

What happens next depends on the action. **Add repository** and **Keybinds** are
views the dashboard already knows how to draw, so the choice is handed to the
running dashboard — `fleet request` drops it in `%APPDATA%\fleet\requests`, the
dashboard picks it up on its next poll, and the form fills the pane it belongs
to. Actions the dashboard cannot draw, such as opening another project, still get
a split of their own.

This is why the menu is scoped to windows containing a fleet pane: the dashboard
marks its pane with a WezTerm user var holding the project name, which is both
the "is fleet here?" test and the address the request is sent to. Elsewhere the
chord is forwarded to the pane untouched.

The binding is a single chord inserted into `config.keys`, not a WezTerm
`leader` — WezTerm allows only one leader and you may already use it. The module
is generated: rebind inside fleet and re-run `apply-keybinds` rather than editing
the Lua.

## Keys

Navigation is Neovim-flavoured, and arrow keys work everywhere too.

| Key | Does |
|---|---|
| `j` / `k` | move down / up |
| `g` / `G` | first / last |
| `ctrl+d` / `ctrl+u` | page down / up |
| `h` / `l` | previous / next tab (dashboard) |
| `l` or `enter` | open the selection (picker) |
| `n` | new project (picker) / new agent / add repository |
| `enter` | open the selection / open an agent, restarting it if needed |
| `m` | manage an agent / manage a repository |
| `p` | pull the highlighted repository |
| `d` | remove a repository |
| `r` | refresh (dashboard) |
| `q` | quit the picker — deliberately does nothing on the dashboard |
| `esc` | cancel a dialog — never closes the dashboard |
| `ctrl+space` | the fleet menu, from any pane |

Adding a repository has no bare key on purpose — it lives in the menu only, so
the dashboard's letters stay free for navigation.

`l` deliberately means two things: next tab in the dashboard, open the selection
in the picker. Each view resolves keys against its own set of actions, so a
shared key is never ambiguous.

Every one of these is configurable through **Keybinds** in the menu, including
the prefix. Changes are saved to `%APPDATA%\fleet\keybinds.json`.

Only keys you actually change are written, so later changes to fleet's shipped
defaults still reach you.

## The dashboard

Two tabs, **Agents** and **Repositories**, switched with `h` / `l` or the arrow
keys. Agents is the one showing on open. Movement stops at the ends rather than
wrapping, so `h` always means left and `l` always means right. Each title carries
its count, so the tab you are not looking at still tells you what is in it.

```
╭┤ fleet — techweb ├──────────────────────────╮
│ Agents (2)    Repositories (1)              │
│ ══════════                                  │
│ backend   feature/login   claude            │
│ backend   fix/auth        nvim     (hidden) │
```

Nothing on the dashboard closes it — not `esc`, not `q`. It is the project's main
pane, so closing is deliberate: **Close this pane** from the fleet menu.

## Agents

An agent is a harness — `claude`, or `nvim` — running in a **worktree of its
own**, bound to one repository and one branch.

Press `n` on the dashboard. The form has three rows — **Repo** and **Base** open a
selection list, **Branch name** is typed:

| Branch name | Base | Result |
|---|---|---|
| given | given | cut that branch from that base |
| given | empty | cut that branch from the default branch |
| empty | given | work on the base branch itself |
| empty | empty | refused — nothing to work on |

The base list shows local branches first, then remote-tracking ones marked
`(remote)`; a remote already checked out locally is not listed twice.

**Opens** picks what runs in the worktree:

- `claude` — the harness on its own.
- `nvim (neo-tree and claude)` — nvim rooted at the worktree, started with
  `:Neotree show` and `:ClaudeCode`, so the tree and Claude are both open.

`m` on the dashboard changes this for an agent that already exists; it applies the
next time that agent starts.

The nvim option runs the commands from your own config, so it depends on
`neo-tree` and `claudecode.nvim` being installed.

fleet creates the worktree beside its siblings, starts the harness in it, and
lists it under Agents.

### Stopping and removing

`m` on an agent opens a menu holding everything that acts on one agent:

- **Change what it opens** — claude, or nvim with neo-tree and claude.
- **Hide or show it in the terminal** — see below.
- **Stop the agent, keep everything** — its pane closes; `enter` starts it again.
- **Remove the agent, keep its files** — fleet forgets it; the worktree stays.
- **Remove the agent and delete its worktree** — asks to confirm, naming any
  uncommitted files that would be lost. Files under `.fleet/` do not count, so an
  agent is never permanently "dirty" from fleet's own bookkeeping.

**The branch is always kept** — removing an agent is not deleting work.

`d` on a **repository** deletes the repository and every worktree under it. It
refuses while any agent is registered on that repository, and the confirmation
lists every worktree that would go and any branch that is not pushed.

Keys follow the tab: `n` and `d` mean agent things on Agents and repository
things on Repositories. Hiding and the harness picker do nothing on the
Repositories tab.

### Hiding an agent

**Hide or show it**, from the `m` menu, hides an agent from the WezTerm tab bar
without stopping it. It stays listed
under Agents marked `(hidden)`, and `enter` brings it back — hiding is a terminal
concern, never a fleet-listing one, so an agent can never be hidden from the
dashboard itself.

WezTerm has no API to hide a tab, so a hidden agent moves to the `fleet-hidden`
workspace. The CLI cannot switch workspaces, so fleet writes the wanted workspace
to `requests/workspace.request` and the generated Lua switches to it from an
`update-status` handler. Re-run `fleet apply-keybinds` after upgrading, or hidden
agents will not come back.

`enter` on an agent **focuses its pane, or restarts it** if the pane is gone. A
hidden agent is **unhidden first**, so it comes back into the project window rather
than opening a window of its own —
after closing the terminal, selecting an agent brings it back in the same
worktree with the same harness. That works because an agent is identified by its
worktree path, so nothing about it depends on a pane surviving.

```
Agents (1)    Repositories (1)
══════════

widgets   feature/login   claude
```

An agent's identity is its **worktree path**, never a pane id — pane ids are
transient and driver-specific, so focusing and restoring work off the path.
Records live in `%APPDATA%\fleet\sessions\<project>.json`; no daemon runs.

Base branch selection prefers your **local** branch when it is ahead of origin,
so cutting a new agent never silently reverts unpushed work.

> Not built yet: reaping agents and tearing worktrees down. Remove a worktree
> with `git worktree remove` for now — fleet's teardown is deliberately absent
> until its dirty check is written, since that is the part that can destroy work.

## Repositories

The Repositories tab is not just a list:

- `enter` opens the repository in nvim with the tree, at its default branch's
  worktree. Not claude — this pane is for pulling and reading code.
- `p` pulls it: `fetch --prune`, then `merge --ff-only` in that worktree, so it can
  never create a merge commit in a checkout an agent is sharing. The row spins
  while it runs.
- `m` manages it — currently changing the **default branch**. That is bookkeeping
  only: it decides what future agents cut from and what `p` fast-forwards, and
  **no worktree is switched**.
- `d` removes it, and `r` re-reads what is on disk.

## Projects and repositories

A **project** is a name pointing at a root folder whose children are
repositories. Multi-repo is the default case, not an add-on.

Repositories are **bare**, with worktrees as their children:

```
<project root>/
  widgets/            bare repository
    main/             worktree for the default branch
```

Adding one either creates a new repository or clones a URL, prompts for the
default branch, and materialises that branch's worktree immediately — including
the initial commit a fresh bare repository needs before `git worktree add` will
work.

Creating a project whose root does not exist asks before creating the directory.

## Configuration

Everything lives under `%APPDATA%\fleet`:

```
projects/<name>.json    a name and a root, with ~ for the home directory
keybinds.json           the prefix and every action binding
fleet.log               failures fleet degraded past, shown by doctor
```

`FLEET_CONFIG_HOME` relocates all of it. `FLEET_MUX` forces a driver.

## Architecture

Vertical slices: one folder per behaviour, holding its command, handler and view.

```
src/Fleet/
  Program.cs      8 lines: parse, dispatch, return an exit code
  Cli/            the composition root
    CommandLine.cs    args -> Invocation (pure)
    Runner.cs         verb -> command
    Commands/         one file per verb
    Composition/      the only place naming a Platform type
  Shared/         pure helpers: Result, keymap config, names, paths
  Ports/          the interfaces covering all I/O
  Ui/             FleetTheme, keymap resolution, prefix recognition
  Platform/       adapters: storage, git, logging, mux drivers
  Features/       Projects, Repositories, Agents, Dashboard, Menu, Diagnostics
tests/Fleet.Tests/
  Architecture/   the layout rules, enforced as tests
```

The rules are checked mechanically, not by convention:

- a slice never references another slice — cooperation goes through the
  composition root
- only `Cli/Composition/` names a `Platform` type — not even `Cli/Commands/` may
- nothing outside the composition root depends on `Cli/`
- `Program.cs` stays under 20 lines
- `Ports/` and `Ui/` depend on nothing but `Shared/`
- no slice styles itself — no scheme, border or `MessageBox` outside `Ui/`
- no source file contains a comment

Multiplexer access goes through `IMuxDriver`, wrapped in `FailSilentDriver` so an
unreachable terminal degrades instead of throwing. Destructive operations are
deliberately excluded from that.

## Development

```powershell
dotnet build
dotnet test
dotnet run --project src/Fleet -- doctor
```

203 tests, none of which need a terminal. Command behaviour runs against
`FakeMuxDriver`; repository behaviour runs against real git in temp directories,
because the plumbing is the point.

`spikes/` holds throwaway experiments kept as evidence — they proved which PTY
libraries survive NativeAOT and what owning a pane would actually cost. See
`docs/DESIGN.md`.

## Documentation

| File | What |
|---|---|
| `docs/DESIGN.md` | the design, every decision, and a dated verification log |
| `docs/phase1.md` | the phase 1 specification |
| `docs/PHASE1-PLAN.md` | the implementation plan it was built from |

`DESIGN.md` also carries a **Non-obvious behaviour** section: the Terminal.Gui
v2 traps, the WezTerm quirks, and the git plumbing this project had to discover.
The code carries no comments by project convention, so that is where the
reasoning lives.

## Lineage

A third attempt at the idea behind [`Redmern/fleet`](https://github.com/Redmern/fleet)
(tmux, Linux, bash and Python) and `fleet-win` (WezTerm, Windows, Go). This one
is built once for both, with the multiplexer behind an interface. It shares no
code with either; both were read as reference, and the traps they had already
paid for are recorded in `DESIGN.md`.
