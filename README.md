# fleet

A TUI orchestration tool for AI coding work across projects that span one or more
repositories. Neovim is the editor, Claude Code is the agent, and a terminal
multiplexer supplies the panes.

Opening a project gives you one window split in two: Claude on the left, the
fleet dashboard on the right. A prefix chord — `ctrl+enter` by default — opens
the fleet menu from **any** pane, including one running nothing but Claude.

> **Status.** Projects, repositories, the menu, configurable keybinds and agents
> all work: you can start an agent in its own worktree, restart it after closing
> the terminal, change what it opens, hide it from the tab bar, stop it, and
> remove it together with its worktree. The orchestrator works too: Claude drives
> fleet over an MCP server under per-project permissions, and a `,`-prefixed
> prompt dispatches a sub-orchestrator that appears on its own tab. Agents report
> their status live through Claude Code hooks. Only the WezTerm driver ships; tmux and the
> embedded driver are designed but unwritten. Linux is built and tested by CI but
> has not been used in anger.

## Requirements

To run fleet:

- [WezTerm](https://wezterm.org) on `PATH` — fleet has nothing to drive without it
- git
- [Neovim](https://neovim.io) 0.9 or later. The main orchestrator and every
  sub-orchestrator run Claude inside nvim through `claudecode.nvim`, with no file tree
  and no file open, so only Claude shows. Agents use nvim by default and can also open
  Claude Code alone. fleet brings its own nvim config with `neo-tree` and
  `claudecode.nvim` (see [Fleet's nvim config](#fleets-nvim-config)), so your own
  config does not need them
- [yazi](https://yazi-rs.github.io), for the folder picker and the file navigator
- Claude Code, which nvim starts in the pane fleet opens on the left
- a Nerd Font in WezTerm, or the branch pills and icons render as boxes

To build it yourself, additionally:

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows: Visual Studio Build Tools 2022 with the **Desktop development with C++**
  workload — NativeAOT needs the MSVC linker and the Windows SDK
- Linux: `clang` and `zlib1g-dev`, for the same reason

## Install

From a published release, with no SDK on the machine:

```powershell
# Windows
irm https://raw.githubusercontent.com/Redmern/fleet-tui/main/scripts/get-fleet.ps1 | iex
```

```sh
# Linux
curl -fsSL https://raw.githubusercontent.com/Redmern/fleet-tui/main/scripts/get-fleet.sh | sh
```

Running on a fork? Point either script at it instead with `-Repo <owner>/fleet`
(PowerShell) or `FLEET_REPO=<owner>/fleet` (sh).

On Linux the binary goes to `~/.local/bin` (or `FLEET_BIN_DIR`). When that folder
isn't on `PATH`, both `get-fleet.sh` and `install.sh` add one line, marked
`# added by fleet installer`, to the startup file of your `$SHELL` and print which
file it was:

| Shell | File |
|---|---|
| bash | `~/.bashrc`, at the top so it runs before the "not interactive, return" guard |
| zsh | `~/.zshenv` |
| fish | `~/.config/fish/conf.d/fleet.fish` |
| anything else | `~/.profile` |

These are files a non-interactive shell reads too, so `ssh <host> fleet bridge`
(what `fleet attach --ssh` runs) finds fleet. Open a new shell, or source that file,
to pick it up now. Running the installer again doesn't add a second line. To keep
your startup files untouched, pass `--no-path` or set `FLEET_NO_PATH=1`; the
installer then only prints the `export PATH=...` line to add yourself.

From source:

```powershell
.\install.ps1     # Windows
```

```sh
./install.sh       # Linux and macOS
```

### Bringing the dependencies with it

Add `-WithDeps` (or `--with-deps`) and the installer sets up the machine first:
WezTerm, Neovim, yazi and git through winget on Windows or the package manager it
finds on Linux. No Neovim config is needed: fleet ships its own (see
[Fleet's nvim config](#fleets-nvim-config)):

```powershell
.\install.ps1 -WithDeps
.\scripts\get-fleet.ps1 -WithDeps
```

```sh
./install.sh --with-deps
sh scripts/get-fleet.sh --with-deps
```

To also clone a Neovim config of your own into `%LOCALAPPDATA%\nvim` / `~/.config/nvim`
(for the `user` nvim config setting), pass `-NvimConfig <git-url>` or set
`FLEET_NVIM_CONFIG`; without either nothing is cloned. Anything already present is
left alone — an existing tool is skipped, a config directory holding a different
remote is not touched, and a matching one is fast-forwarded. A config that ships its
own `bootstrap.sh` is reported rather than run.

Debian and Ubuntu carry no WezTerm package, so there `--with-deps` installs the rest
and points at [wezterm.org/installation](https://wezterm.org/installation).

Either route ends by running `fleet setup`, which writes the WezTerm module, wires
your WezTerm config, and lists anything still missing with the command that fixes
it:

```
fleet setup
  ok  wezterm        found on PATH
  ok  git            found on PATH
  --  nvim           missing - agents cannot open it
  ok  fleet.lua      C:\Users\you\.wezterm\fleet.lua
  ok  wezterm config wired C:\Users\you\.wezterm.lua
  ok  nvim config    fleet's own, in C:\Users\you\AppData\Local\fleet-nvim (nvim 0.11.4)
  glyph check     develop ↑1 ●
still to do:
  nvim           winget install Neovim.Neovim
```

It is safe to run again: an already-wired config is left alone.

On Windows, `install.ps1` also creates a Fleet launcher
(`scripts\windows\Install-FleetShortcut.ps1`): a Start Menu `Fleet.lnk` and a
Windows Terminal profile named "Fleet" (a fragment under
`%LOCALAPPDATA%\Microsoft\Windows Terminal\Fragments`; skipped when your
`settings.json` already has one). Windows doesn't let scripts pin to the taskbar,
so pin it yourself: right-click Fleet in Start, "Pin to taskbar". Fleet windows
then group under that button. A `Fleet.lnk` you made yourself is kept (`-Force`
on the script replaces it); `-NoShortcut` skips the launcher altogether.

`.\install.ps1 -Uninstall` reverses a Windows install. Add `-Purge` to delete
`%APPDATA%\fleet` as well. On Linux, `./install.sh --uninstall`
removes the binary and the marked `PATH` line.

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
fleet mcp --project <name>  serve the MCP tools over stdio (Claude calls this)
fleet mcp --head            serve the head orchestrator's cross-project tools
fleet head [--voice]        run the head orchestrator's Claude (the alt+o chord does this)
fleet dispatch "<task>" --project <name>
                            spin up a sub-orchestrator for a task
fleet report --caller <slug> --status <s> --project <name> -- <summary>
                            a sub-orchestrator reports its own status
fleet doctor                check the environment
fleet theme list|get        the themes, and the active one
fleet theme set <name>      switch theme; running fleet windows follow live
fleet theme sync            follow omarchy's current theme
fleet theme install omarchy hook omarchy so fleet follows every theme switch
fleet version               show the version, and check for an update
fleet update                download and install the latest release
fleet attach                attach this terminal to fleetd, starting it if needed
fleet daemon stop           stop fleetd and forget its projects; the next start is fresh
```

After `fleet update`, a running fleetd keeps the old binary. `fleet daemon stop` then
`fleet attach` moves it onto the new one. Stopping closes every pane, so any agent
in the middle of a task is interrupted, and forgets which projects were open: the next
start opens only the project you open. (A crash or restart without `stop` still restores
everything.)

`fleet doctor` is the end-to-end smoke test: it reports the config directory, the
selected multiplexer driver and whether it responds, the git version, every saved
project, and any failures fleet swallowed.

`fleet version` and `fleet update` check `github.com/Redmern/fleet-tui/releases` by
default — set `FLEET_REPO=<owner/name>` (the same variable `scripts/get-fleet.*`
use) to check a fork instead. `fleet update` downloads the release asset for your
platform, verifies it against the published `.sha256`, and replaces the running
binary in place; a fleet already running keeps its old code until it is reopened.
Only Windows and Linux x64 have published binaries — build from source with
`install.sh`/`install.ps1` elsewhere.

fleet works from any terminal, not just a wezterm pane. Inside a pane it talks to
the mux named by `WEZTERM_UNIX_SOCKET`; outside one it finds a live wezterm socket
itself. If several wezterm windows are running as separate GUI processes, it targets
the most recently used one that answers.

## The menu

```powershell
fleet apply-keybinds
```

writes the Lua module from your current keymap — `~/.wezterm/fleet.lua` on Windows,
`~/.config/wezterm/fleet.lua` elsewhere. `fleet setup` adds these lines to your
WezTerm config for you; by hand it is:

```lua
local fleet = require 'fleet'
fleet.apply(config)
```

`ctrl+enter` then opens **fleet's own menu** — drawn by fleet, styled like the rest
of it, one key per entry. It opens as a tab, so no pane is resized, and closes
itself when done. Each entry and section header has a Nerd Font icon, like the
`ctrl+s` popup (the status bar already assumes a Nerd Font).

`backspace` goes back one level anywhere in the menu: from a screen to the menu or
submenu it was opened from (Permissions back to Fleet config, Fleet config back to
Settings, Settings back to the menu), and on the top level it closes the menu like `esc`.
`esc` still closes the whole menu at once. In a text field `backspace` deletes what you
typed and only goes back once the field is empty; the key-capture dialog in **Keybinds** records it as a key.

```
╭─ fleet menu ─────────────────────────╮
│                                      │
│                                      │
│     Q     Quit                       │
│     m     Dashboard                  │
│     p     Switch                     │
│     l     List agents                │
│     f     Files                      │
│     s     Settings     ›             │
│                                      │
│                                      │
│   enter select   q/esc close   ...   │
```

Each row is its key, then its icon, then the label. A submenu with sections (Settings, Fleet
config) puts a quiet caption over each section and a blank line between them.

**Show keybinds** (`K` in Settings, on by default; one switch for every project and the project
picker) hides the keys when it is off: the key column in every menu and the keys on every button bar
(dashboard, pickers, settings screens), which then show only their labels. The keys still work.
While it is off, anywhere outside a text field:

- **hold `/`** to show the keys while you hold it. fleet sees the key repeat rather than the
  release (terminals don't report key releases to fleet's panes), so the keys go away a moment after
  you let go, and a quick tap shows them for about a second;
- **`?`** shows them until you press `?` again.

The fleet menu, the pickers and the settings, log, notification, remote, secrets, agent-list and
keybinds screens have an info icon in the top-left corner with the reveal key (`?`)
beside it while the keys are hidden; clicking it does the same as `?`. The top-right corner is a
close button (with `esc` beside it while the keys are shown), and the select and back buttons on the
bar are icons too, as are all the buttons on the remote machines screen. A change to the setting reaches open screens,
such as the dashboard, within a second. Both keys can be rebound under **navigation** in Keybinds;
the editor refuses a key that another fleet action already uses, in either direction.

- **Quit** (`Q`, deliberately shifted) closes every pane of the project, including hidden agents in
  their own workspace, so nothing is left running invisibly. Agent records are
  already on disk, so reopening the project lists them again.
- **keybinds** opens the editor in the dashboard and **focuses that pane**, so you
  end up looking at the view you asked for even from an agent pane.
- **main pane** jumps to the dashboard. This one never starts a fleet process.
- **List agents** opens a list with **Open** and **Hidden** tabs, switched with
  `h`/`l` or the arrows; `enter` goes to the agent.
- **Notifications** (`n`) opens the notification center: an **All** tab and one tab per
  project, switched with `h`/`l` or the arrows. `enter` goes to the agent, `d` dismisses the selected notice, `D` all shown ones,
  `b` turns the terminal bell on or off and `t` the desktop toasts.

### Remote machines

**Remote machines** (`r` in the fleet menu's Settings) connects this fleet to fleet on another
machine over ssh. `n` asks for the ssh host (for example `user@homelab`); fleet runs
`ssh <host> fleet bridge` itself and, when ssh wants a password or asks to trust a new
host key, shows the question in a small dialog. `d` disconnects, and `enter` answers or
retries a connection. The remote needs this fleet on its PATH, or name it with
`FLEET_REMOTE_COMMAND`.

A machine that connected once is remembered (`remotes.json` in the fleet config folder)
and stays in the list after a disconnect or a restart, muted as `known · not connected`;
`enter` reconnects it. `e` gives a machine a nickname (empty clears it), which fleet shows
wherever it names the machine, with the ssh host next to it; `x` forgets a machine.

While a machine is connected, **Switch project** gets tabs: **All**, **this machine** and
one tab per remote, named after its nickname or the remote's hostname. `h`/`l` or the arrows switch
tabs, and it opens on the tab of the machine you are on. A machine's tab lists every project
saved there, running or not (running ones say `open`); opening one that is not running
starts it on that machine first. `enter` shows a remote project
in this window; `SHIFT` opens it in a new window attached over ssh.

**this machine** and every machine's tab end with **+ New project...** (`n`). On this
machine it opens the same New project form as the startup picker and then opens the new
project. On a remote it shows that machine here and opens the remote's own New project
form, so the project and its folder are made there; a machine with no projects yet still
has this entry, so its tab is never empty.

A remote project shown here is drawn by the remote fleet and shown by yours: its tab bar
reads `project @machine`, and keys, the mouse, splits, tabs, floats, copy mode and the
fleet menu (`ctrl+enter`) all act on the remote. Switch project always opens this machine's
switcher, from `ctrl+s s` or from the remote's menu, so you can always get back to a project
here. One window at a time follows a given remote machine;
disconnecting it closes its view here and leaves its panes running there.

The remote project a window shows here also counts as one of that window's projects for
notifications: its open notices add to the pill's `+N`, a new one rings the bell and shows
a toast (with this machine's settings), and the notification center gets a tab for it,
`homelab @machine`, where `d` dismisses on the remote and `enter` shows the project here.
Notifications always opens this machine's center, whether from the remote's menu (`n`) or a
click on the remote's notice pill, the same way Switch project and the head stay here; only
fleet used directly on the remote machine opens the remote's own center.

### Sessions

A session is a saved set of projects for one window. **Save session** (`w` in
the fleet menu's Settings) stores the window's projects in their order, which one was showing, and
for a remote project the machine it runs on; saving under an existing name updates it.

Running plain `fleet` in a terminal then shows **Projects** and **Sessions** tabs (`h`/`l`
or the arrows; the tabs appear once a session exists). Opening a session starts its
projects if they are not running, reconnects its remote machines (asking for a password
if ssh needs one), and opens one window holding exactly those projects. `d` removes a
session; the projects themselves stay. Sessions are opened from the picker only, not
from inside fleet.
### Notifications

Every project's dashboard has a **Notifications** tab next to Agents, Subs and Repositories.
The dashboard watches its agents and opens a notice when one:

| Mark | Reason |
|---|---|
| `?` | asks a question and waits for your answer |
| `!` | waits on a permission prompt |
| `✓` | is done and its work is ready for review |
| `✗` | failed, or its pane disappeared |
| `…` | shows the spinner with no new output for 10 minutes |
| `↕` | has a branch that conflicts with its base or is 20+ commits behind it |

Agents report their own status through Claude Code hooks: fleet writes a `fleet hook`
entry into each agent worktree's and orchestration folder's `.claude/settings.local.json`,
next to any hooks you have there. Question, permission and stall notices then come from
those reports (a stall is 10 minutes of work with no new hook event), and the agent row
shows working, waiting, stalled or idle. An agent with no report yet, or a project with
**Live status via hooks** turned off in the settings (`enter` on its row), falls back to
reading the pane. `fleet doctor` shows whether the hooks are wired.

A notice resolves on its own when its cause goes away, and `d` dismisses it. Resolved and
dismissed notices are kept for a day: greyed out in the dashboard's tab, and in the
notification center they move to its **History** tab, so All and the project tabs show only
open notices. They are kept per project in
`%APPDATA%\fleet\notices`.

When a notice opens, fleet shows a desktop toast (on by default) and can ring the terminal
bell (off by default); toggle both in the notification center. The embedded multiplexer's
tab bar adds the counts to the project pill, `fleet ● 2 +3`: two open notices in this project,
three in other open projects.
Click it to open the notification center.

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
| `n` | new project (picker, switcher) / new agent / add repository |
| `enter` | open the selection / open an agent, restarting it if needed |
| `m` | manage an agent / manage a repository |
| `e` | open nvim (neo-tree) in the selected agent's or sub's folder, beside its pane; focuses it if already open |
| `p` | pull the highlighted repository |
| `d` | remove a repository |
| `r` | refresh (dashboard) |
| `q` | quit the picker — deliberately does nothing on the dashboard |
| `esc` | cancel a dialog — never closes the dashboard |
| `backspace` | back one level in the fleet menu; in a text field it deletes, and goes back only when the field is empty |
| `ctrl+enter` | the fleet menu, from any pane |
| `alt+o` | show or hide the head orchestrator in voice mode, from any pane of a fleet window |

Adding a repository has no bare key on purpose — it lives in the menu only, so
the dashboard's letters stay free for navigation.

`l` deliberately means two things: next tab in the dashboard, open the selection
in the picker. Each view resolves keys against its own set of actions, so a
shared key is never ambiguous.

`k` is the exception: in a menu that lists **Keybinds**, `k` opens it rather than
moving up. Use the up arrow there, or rebind one of the two.

Every one of these is configurable through **Keybinds** in the menu, including
the prefix. Changes are saved to `%APPDATA%\fleet\keybinds.json`.

Only keys you actually change are written, so later changes to fleet's shipped
defaults still reach you.

On the built-in multiplexer, the `ctrl+s` popup has submenus: `ctrl+s f` › *float*,
`ctrl+s w` › *project*, `ctrl+s q` › *session* (`ctrl+s f t` shows or hides the floats).
`esc` closes the popup and `backspace` goes up a level. Prefix keys in
`embedded-keys.json` can be sequences, and `groups` names them:
`{ "prefixKeys": { "g s": "split-down" }, "groups": { "g": "git" } }`. Binding a group's
key as a single key (`"f": "float-new"`) gives you the old flat key back. Groups and
the focus / resize / tab rows show Nerd Font icons; `icons` changes a group's icon
(`"none"` removes it) and `"showIcons": false` turns them off. See
`docs/DESIGN.md` › *Keys* for the full table and the rules.

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

The nvim option uses fleet's own nvim config by default, which brings `neo-tree` and
`claudecode.nvim`; with the `user` setting it runs the commands from your own config,
which then needs both plugins. See [Fleet's nvim config](#fleets-nvim-config).

### Fleet's nvim config

fleet ships a small Neovim config (`nvim/` in this repository, embedded in the binary):
lazy.nvim with `neo-tree`, `claudecode.nvim` and their dependencies (`plenary`, `nui`,
`nvim-web-devicons`). `fleet setup` writes it to `%LOCALAPPDATA%\fleet-nvim` on Windows
or `~/.config/fleet-nvim` elsewhere (`$XDG_CONFIG_HOME/fleet-nvim` when that is set),
and fleet refreshes it whenever it starts an nvim pane, so hand edits there are
overwritten.

Every nvim fleet starts gets `NVIM_APPNAME=fleet-nvim`, so nvim reads that config and
keeps its plugins, state and shada in separate `fleet-nvim` folders. Your own nvim
config and plugin folders are never read or touched. `fleet setup` also installs the
plugins (headless, once; it needs git and network), so nvim panes start straight away. `NVIM_APPNAME`
needs Neovim 0.9 or later; `fleet setup` and `fleet doctor` check the version.

**Nvim config** (fleet menu > settings > fleet config, `N`) switches between `fleet`
(the default) and `user`. `user` starts nvim exactly as before, with your own config, for
when you already have `neo-tree` and `claudecode.nvim` set up the way you like. It is
one setting for the whole machine (`nvim.json` in the fleet config folder) and applies
to nvim panes started after the change.

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

**Auto-close idle agents** (fleet menu > settings > fleet config, `i`) does the stop for you. It is
per project and off by default. When it is on, the project's dashboard stops an agent or
sub-orchestrator that reported `done` or `failed` and has been idle for the threshold
(30 minutes by default). "Idle" means no new report and no change in its pane's text
in that time. It never closes the main orchestrator, anything still working, anything asking
you a question or for permission, or the active pane. The log says which agent it closed
and why. `enter` reopens the agent, and claude continues the same conversation
(`--continue`).

**Models** (fleet menu > settings > fleet config). Every claude fleet starts gets a session
name (`fleet-head`, `<project>-main`, `<project>-sub-<slug>`, `<project>-<repo>-<branch>`), and a
model and effort per role. `H` sets the head's (all projects), `M` the main orchestrator's, `S`
the sub-orchestrators' and `R` the repo agents' (per project). Pick a model alias (`sonnet`,
`opus`, `haiku`, `fable`), type an ID, or `inherit` to pass no `--model` and use the Claude
profile's default; then an effort (`low` to `max`) or `inherit`. Sub-orchestrators default to
`sonnet` at `medium`; the rest inherit. A change applies to panes started after it; a running
pane keeps what it was started with.

`d` on a **repository** deletes the repository and every worktree under it. It
refuses while any agent is registered on that repository, and the confirmation
lists every worktree that would go and any branch that is not pushed.

Keys follow the tab: `n` and `d` mean agent things on Agents and repository
things on Repositories. Hiding and the harness picker do nothing on the
Repositories tab.

### Opening an editor

`e` on an agent or sub-orchestrator opens nvim with neo-tree in its folder (the
worktree, or `.fleet/orchestrations/<slug>` for a sub), titled `<repo>/<branch> editor`.
It splits beside the agent's pane; if that pane is closed or hidden it opens in the
project window instead. Pressing `e` again focuses the editor rather than opening a
second one. **Open editor here** in the fleet menu does the same for the agent whose
pane you are in; the entry only shows when the menu was opened from such a pane. The
editor is a helper, not the agent: hiding or stopping the agent
leaves it alone, and an agent with only its editor open still counts as not running.

### Hiding an agent

Every agent and sub-orchestrator fleet starts, from the dashboard, the MCP tools or
`dispatch`, **starts hidden**: it runs, takes its first task and later `tell_agent`
messages in the background without opening a pane or taking focus. Open it yourself
when you want to watch it.

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

## The orchestrator

The pane fleet opens on the left — Claude — is the project's orchestrator, and it
can drive fleet itself through an MCP server. `fleet mcp --project <name>` speaks
the Model Context Protocol over stdio; fleet registers it for you in the project's
`.mcp.json` and pre-approves it in `.claude/settings.local.json` every time the
dashboard starts, so Claude sees the `fleet` tools with no first-run prompt. (Claude
Code only honours a project's approval in a *trusted* workspace; fleet records that
trust in `~/.claude.json` for every folder it opens, so there is no `/mcp` step.
`fleet doctor` reports whether each project is registered and enabled.)

**Permissions.** Press `s`, `c`, then `p` in the menu (Settings, Fleet config, then Permissions) to say, per project and per tool, whether the
orchestrator may do the action, must ask, or can't. Reads are allowed by default;
writes ask. An "ask" can prompt in the fleet dashboard, in Claude's own permission
prompt, or both. When the prompt lives in the dashboard, the running dashboard shows
an Allow/Deny dialog — even though the MCP call is a separate process — and the
answer travels back over the filesystem. If no dashboard is running, the tool fails
fast with a clear error instead of hanging.

The same screen has three git gates that fleet writes as Claude permission rules into
every orchestrator and agent folder: **Agents commit changes** (`git commit`), **Agents
push changes** (`git push`) and **Agents merge pull requests** (`gh pr merge`, in Bash and
PowerShell). Each is `auto`, `ask` or `no`; commit and push ask by default, merge is
`auto`.

**Dispatch.** Type a prompt in the main pane beginning with the dispatch trigger — a
comma by default, configurable in the permissions screen — and instead of answering,
the orchestrator spins up a *sub-orchestrator*: its own Claude in a hidden pane,
working under `<project>/.fleet/orchestrations/<slug>/`, with a file browser split
alongside. A sub-orchestrator uses the same MCP tools under the same permissions,
creates repo agents that are stamped as its own, and reports back with
`fleet report`. The **Subs** tab (between Agents and Repositories) groups each
sub-orchestrator with the agents it created and shows its status as an icon — working,
waiting for input, idle, stalled, done or failed; hover it for the name — until you
remove it. Agent rows on the Agents tab use the same icons. A sub's report is not sent to
the main orchestrator; its questions, stalls and done/failed show up in the
notification center like an agent's.

**Dispatch to one repository.** When the orchestrator calls the `dispatch` tool with a
`repository` (and optionally a `branch`), fleet skips the sub-orchestrator and starts a
repo agent on that repository with the task, as `new_agent` would; the call must pass both
the `dispatch` and the `new_agent` permission. Without a branch, the branch is named from
the task the way a sub's slug is. A sub-orchestrator is still used when AIDLC applies to
the task, or when it is research (`research: true`, or the `research` profile); a research
sub is told to do the work itself with subagents instead of starting repo agents. A typed
dispatch (`,task`) always starts a sub-orchestrator.

**Managing subs.** The orchestrator (or a sub) can manage sub-orchestrators with three
MCP tools, each with its own row in the permissions screen:

| Tool | What it does | Default |
|---|---|---|
| `list_subs` | every sub with its slug, status, whether its pane is open, its last report (time and summary) and its agents | allow |
| `stop_sub(slug)` | closes the sub's pane; its record and folder stay | ask |
| `remove_sub(slug, delete_folder, remove_agents)` | fleet forgets the sub; its folder stays unless `delete_folder` is true | ask |

`remove_sub` refuses a sub that is still `working` (a sub that never reported counts as
working; stop it and remove it from the Subs tab instead) and refuses the caller's own sub.
It leaves the sub's agents alone unless `remove_agents` is true: they stay registered,
keep running, and become top-level agents on the Agents tab. With `remove_agents`, each
agent is removed with its worktree, except one with uncommitted changes or commits that
are on no remote (and not on its base branch); that one is kept as a top-level agent and
named in the result. Removing a sub from the Subs tab without throwing away its agents
moves them to the Agents tab the same way. The approval prompt says when a `remove_sub`
would delete the folder or the agents.

**AIDLC.** A sub-orchestrator can run a structured process instead of free-form. Press
`A` in the menu's Settings > Fleet config submenu to set it per project:

- **Mode** — `off` (default), `on` (every dispatch), or `manual` (only when the task
  starts with a profile prefix).
- **Profile** — how much ceremony a task gets: `express` (default; short spec, build,
  verify, review, PR), `bugfix` (reproduction first), `feature` (spec, plan, walking
  skeleton; may span repositories), `refactor` (no behaviour change) or `research`
  (investigate and report, no code). Name one per task with a prefix after the trigger,
  `,feature: add oauth login`. With the mode `on`, the `profile` argument of the
  `dispatch` tool also picks one; otherwise the project default applies.
- **Autonomy** — `guided` checks in with you after each unit; `automatic` goes on by
  itself. A failure always stops and asks.
- **Parts** — switch off the spec, plan or deliver gate (the stage still runs, without
  waiting for you), the walking skeleton, or the verify, review or learn stage (skipped).

fleet writes the process into the sub-orchestrator's CLAUDE.md, keeps the task's record
next to it (`state.json`, and `audit.jsonl` for what happened), and tells it to stop and
ask you at each approval point. `.fleet/config/aidlc.md` adds your own notes to that
process.

## The head orchestrator

Every project has its own orchestrator. The **head** is one Claude above all of them:
you tell it "go to techweb and have its orchestrator dispatch: add a login page", and it
switches the window to techweb and types `,add a login page` into techweb's orchestrator
as if you had.

- **`alt+o`** shows the head from any pane of a fleet window; pressing it again hides it.
  Hiding never stops it: it is one Claude session that lives across projects and
  windows, and comes back with the conversation where you left it. After a restart of
  WezTerm it resumes the last conversation (`claude --continue`).
- The head always opens in **voice mode**: Claude Code's voice dictation is on (hold space
  to talk). A head started in text mode with `fleet head` (no `--voice`) is restarted in
  voice mode by the chord; the restart resumes the conversation (`claude --continue`), but
  a turn the head is in the middle of is cut off.

  Voice is a Claude Code setting, `voice.enabled`, so fleet starts the head with
  `claude --settings <file>` holding `{"voice":{"enabled":true}}` or `false`. That is why
  switching needs a restart: `/voice` saves to your user settings, which the head's
  `--settings` file outranks, so don't use `/voice` in the head. Voice needs a claude.ai
  login, as it does anywhere in Claude Code.
- The chord is direct, with no prefix, and rebindable under **Keybinds** in the
  *anywhere, no prefix* group. On WezTerm, re-run `fleet apply-keybinds` after changing
  it; the built-in multiplexer picks them up on its next attach or `prefix q r`.
- **On the built-in multiplexer** the head is a real float: 80% of the screen, over
  whichever project the window shows. Hiding moves it out of sight without stopping it,
  and showing it from another project brings the same head along. `embedded-keys.json`
  can still rebind or unbind the chord (`"keys": { "alt+o": "none" }`).
- **WezTerm has no floating panes**, so there the head lives in a workspace of its own,
  `fleet-head`: the chord switches the window into it and back to the workspace you came
  from. Your project windows stay exactly as they were.

The head runs in `%APPDATA%\fleet\head`, where fleet writes its `CLAUDE.md` (its role and
tools) and registers `fleet mcp --head` as its MCP server every time it starts. Its tools:

| Tool | Does |
|---|---|
| `list_remotes` | the machines the head can act on: `local` (the machine fleet was opened on) first, then each remote's nickname, ssh host, last connection and whether it is connected now |
| `list_projects` | the projects of one machine (`remote`, default `local`), open or closed, and how many relayed prompts wait for it |
| `list_remote_projects` | the projects per machine: this machine first, then every remote machine fleet knows (nickname and ssh host, `connected` or `known · not connected`), each project `open` or `closed` |
| `switch_project` | shows a project, opening it first if it is closed |
| `menu_action` | hands a dashboard action (`new-agent`, `add-repository`, `keybinds`, ...) to a project's dashboard and shows it |
| `list_agents` | the agents of one project, or of every open project, as a flat list |
| `project_structure` | one project's full structure: its repositories, each sub-orchestrator with the agents it started indented under it (status and last report), and the agents under no sub-orchestrator |
| `tell` | types a plain message into a project's main orchestrator; never dispatches |
| `relay` | types a dispatch prompt into a project's main orchestrator |
| `show_agent` | shows one agent's or sub-orchestrator's pane in its project's window, by name, starting it if it is not running |
| `hide_agent` | hides one agent's or sub-orchestrator's pane by name, without stopping it |

**Showing and hiding panes.** `show_agent` and `hide_agent` act on a named target, not on
the dashboard's selection: `repository` + `branch` for an agent, `sub` alone for a
sub-orchestrator, or `sub` + `repository` + `branch` for an agent that sub-orchestrator
started. They do what the dashboard's hide toggle and the project's `set_agent_visible` and
`open_agent` tools do: a hidden pane moves back into the project's window, a visible one
moves out of sight, and a stopped agent is started (and its project opened if it is closed).
Showing a visible pane or hiding a hidden one succeeds and changes nothing. An unknown
project, sub-orchestrator, repository or branch is an error that lists the names there are.

**Relaying.** `relay` puts the project's dispatch trigger in front of the task and types it
into that project's orchestrator, so the orchestrator's own hook dispatches a
sub-orchestrator exactly as if you had typed it. If the project is closed, the head opens
it and waits up to 90 seconds for its Claude. If that Claude is busy (a spinner, or a
question or permission prompt on screen), the prompt is **queued** and the head is told
so; fleet types it in as soon as the orchestrator is idle, in order, for up to an hour.
The queue lives in the head's MCP server, so it is lost if the head's Claude exits.

**Telling.** `tell` is the head's tool for everything that is not a dispatch: asking the
orchestrator for a status update, a follow-up, an answer to its question. It types the
message as-is (a leading dispatch trigger is stripped, so it can never dispatch), with the
same opening, queueing and typing as `relay`. The head uses `relay` only when you
explicitly ask it to dispatch.

**Messages instead of typing.** When the orchestrator's Claude takes cross-session messages
(Claude Code 2.1.234 or later on Windows, with live status hooks on), `relay` and `tell` type
nothing. They answer the head with that session's address, and the head sends the message
itself with Claude's `SendMessage`. Claude Code delivers it at the orchestrator's next tool
call, or starts a turn when it is idle, so nothing is queued and nothing has to be read off
the screen. A relay sent this way asks the orchestrator to call its `dispatch` tool, so the
project's `dispatch` rule is checked on that call rather than by the head. `tell_agent`
works the same way for an orchestrator's agents, and its `typed: true` falls back to typing
when `SendMessage` can't reach the session. A project or agent whose Claude reported no
inbox (an older Claude, status hooks off, a remote machine's head) is typed into as before.

**Finished agents push.** A sub-orchestrator doesn't poll `list_agents` to learn that an agent
finished: it subscribes to the agent's session with `SendMessage`'s `notify_when_idle`
(`new_agent` names the session) and checks `list_agents` when the notice comes. When an agent
reports `done` or `failed`, the `report` tool's result tells it to send its one-line summary
to its owner's session (its sub-orchestrator, or the project's main orchestrator) with
`SendMessage`. The owner needs a live inbox; otherwise nothing extra happens.

**Remote machines.** `list_remote_projects` reads what fleetd already knows: the live
links (as **Remote machines** shows them) and `remotes.json`. It never opens an ssh
connection, so a machine that is not connected is listed with its state and no projects.

`list_projects`, `switch_project`, `menu_action`, `list_agents`, `project_structure`, `tell`,
`relay`, `show_agent` and `hide_agent` take an optional `remote`: a nickname from
`remotes.json` (never an ssh host). Leave it out, or pass `local`, for the machine fleet was
opened on. A remote that is not connected is connected first, the same way **Remote
machines** does; if ssh asks a question (a password, a host key), the head says so and you
answer it there. `switch_project` shows the remote project in this window; `relay`, `tell`,
`list_agents`, `project_structure`, `menu_action`, `show_agent` and `hide_agent` run on the
remote's own fleet over the existing ssh
link, so that project's permissions apply there exactly as they do locally. A remote fleet
never opens ssh to another remote: everything goes through the origin.

**One head.** The head always runs on the origin. Pressing its chord while you look at a
project on a remote machine shows the origin's head over it, not a head on that machine.

**Permissions.** What the head does inside a project goes through that project's own
permissions (**Settings → Fleet config → Permissions**): `relay` is the project's `dispatch` rule, `tell`
its `tell_agent` rule and `list_agents` its `list_agents` rule. `project_structure` checks each part
under its own rule (`list_repositories`, `list_subs`, `list_agents`) and shows a refusal in that
part's place. `show_agent` and `hide_agent` are its `set_agent_visible` rule, or `open_agent`
when the agent has to be started. *Forbid* refuses; *ask* shows the Allow/Deny dialog in
that project's dashboard, whichever channel the rule names, because the head's own Claude
cannot tell projects apart. Switching projects, opening menus and showing or hiding the
head are navigation and always allowed.

## Configuration

Everything lives under `%APPDATA%\fleet`:

```
projects/<name>.json    a name and a root, with ~ for the home directory
keybinds.json           the prefix and every action binding
settings/<name>.json    the per-project tool permissions, dispatch trigger and AIDLC settings
head/                   the head orchestrator's folder: CLAUDE.md, .mcp.json, voice settings
approvals/<name>/       in-flight MCP approval requests (transient)
current-theme           the active theme's name (see Themes)
themes/<name>.toml      your own themes
fleet.log               failures fleet degraded past, shown by doctor
```

`FLEET_CONFIG_HOME` relocates all of it. `FLEET_MUX` forces a driver.

## Themes

fleet's screens, its fleetd chrome and the WezTerm tab and status pills
(`fleet-theme.lua`) all draw from one palette. Fifteen themes ship with it:

```
catppuccin-mocha (default)  catppuccin-latte  tokyo-night     gruvbox-dark
gruvbox-light               nord              dracula         solarized-dark
solarized-light             rose-pine         everforest      kanagawa
one-dark                    ayu-dark          github-dark
```

```powershell
fleet theme list            # * marks the active one; custom themes say (custom)
fleet theme set tokyo-night # names are loose: "Tokyo Night" works too
fleet theme get
```

Every running dashboard, menu and picker, and fleetd, watches the theme and
redraws within a moment of a switch. The first of them to notice also rewrites
`fleet-theme.lua` (only if `fleet setup` installed it) so WezTerm reloads its
colors. `fleet theme set` does that itself, so it works with nothing running.

### The current-theme file

The active theme is the file `current-theme` in the config directory
(`%APPDATA%\fleet\current-theme`, `~/.config/fleet/current-theme`). It is plain
UTF-8 text: the first line that is neither blank nor a `#` comment is the theme's
name, a built-in name or the file name of a custom theme. Surrounding spaces and
case don't matter. No file, or a name fleet doesn't know, means Catppuccin Mocha.

Another app can switch fleet's theme either by running `fleet theme set <name>`
or by writing that file; write it whole (to a temporary file, then rename) so a
watcher never reads half of it.

```
# written by my-theme-switcher
gruvbox-dark
```

### Custom themes

Drop a TOML file in `themes/` in the config directory; its file name is its theme
name. Every key is optional: whatever it leaves out comes from `inherits` (Catppuccin
Mocha when that is missing too). Colors are `#rrggbb` (or `0xrrggbb`).

```toml
# %APPDATA%\fleet\themes\midnight.toml  ->  fleet theme set midnight
title    = "Midnight"
inherits = "tokyo-night"
light    = false

crust    = "#0b0c10"   # deepest background; also the text on accent buttons
mantle   = "#101118"
base     = "#14151f"   # the screen background
surface0 = "#20222f"   # chips, pills, input fields
surface1 = "#2c2f40"   # the focused row
overlay0 = "#5a5f7a"   # hints, borders, dimmed text
subtext0 = "#a0a6c0"   # muted text
text     = "#d0d6f0"
blue     = "#7aa2f7"   # keys, section headers, buttons
lavender = "#bb9af7"   # titles and the active tab pill
green    = "#9ece6a"
yellow   = "#e0af68"
red      = "#f7768e"
cursor   = "#c0caf5"

color0  = "#15161e"    # color0..color7 are the ANSI colors WezTerm uses,
color9  = "#ff7a93"    # color8..color15 the bright ones
```

Editing a custom theme that is active recolors running windows as well.

### Omarchy

```bash
fleet theme install omarchy
```

That one command adds a line running `fleet theme sync` to
`~/.config/omarchy/hooks/theme-set` (creating it, or appending to an existing hook
without touching what is there, ahead of a trailing `exit`), and syncs right away.
From then on every Omarchy theme switch switches fleet too.

`fleet theme sync` reads `~/.config/omarchy/current/theme`: its name (from
`current/theme.name`, or the folder the link points at), `colors.toml` or
`alacritty.toml`, and `light.mode`. A name that matches a built-in theme of the
same lightness (`catppuccin` is Mocha, `gruvbox` is Gruvbox Dark) uses that theme.
Anything else gets a palette derived from Omarchy's own colors, saved as
`themes/omarchy.toml` and made active.

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
