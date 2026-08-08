# fleet

A TUI orchestration tool for AI coding work across projects that span one or more
repositories. Neovim is the editor, Claude Code is the agent, and a terminal
multiplexer supplies the panes.

Opening a project gives you one window split in two: Claude on the left, the
fleet dashboard on the right. A prefix chord — `ctrl+space` by default — opens
the fleet menu from **any** pane, including one running nothing but Claude.

> **Status: phase 1.** Projects, repositories, the dashboard shell, the menu and
> configurable keybinds all work. Agents do not exist yet — the Agents pane is a
> placeholder until phase 2. Only the WezTerm driver ships; tmux and the embedded
> driver are designed but unwritten. Nothing has been built or run on Linux.

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

## The menu

```powershell
fleet apply-keybinds
```

writes `~/.wezterm/fleet.lua` from your current keymap. Add to `.wezterm.lua`:

```lua
local fleet = require 'fleet'
fleet.apply(config)
```

`ctrl+space` then opens a centred overlay listing fleet's actions, with fuzzy
filtering.

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
| `l` or `enter` | open the selection |
| `n` | new project (picker) |
| `r` | refresh (dashboard) |
| `q` | close the pane |
| `esc` | cancel a dialog — never closes the dashboard |
| `ctrl+space` | the fleet menu, from any pane |

Adding a repository has no bare key on purpose — it lives in the menu only, so
the dashboard's letters stay free for navigation.

Every one of these is configurable through **Keybinds** in the menu, including
the prefix. Changes are saved to `%APPDATA%\fleet\keybinds.json`.

Note that a saved keymap overrides the shipped defaults completely. Once you
rebind anything, later changes to fleet's defaults will not reach you.

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
  Program.cs      composition root - the only file naming a Platform type
  Shared/         pure helpers: Result, keymap config, names, paths
  Ports/          four interfaces covering all I/O
  Ui/             FleetTheme, keymap resolution, prefix recognition
  Platform/       adapters: storage, git, logging, mux drivers
  Features/       Projects, Repositories, Dashboard, Menu, Diagnostics
tests/Fleet.Tests/
  Architecture/   the layout rules, enforced as tests
```

The rules are checked mechanically, not by convention:

- a slice never references another slice — cooperation goes through the
  composition root
- `Features/` never references `Platform/`; only `Program.cs` may
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
