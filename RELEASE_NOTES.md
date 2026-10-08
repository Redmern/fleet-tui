# Release notes

What changed in each fleet release, newest first. fleet shows this file under Settings > What's new, and the
release workflow uses the entry for a tag as the GitHub release text. Every release needs an entry here, above
the earlier builds: `## <version> (<yyyy-mm-dd>)` followed by 2-6 short `- ` bullets.

## 0.1.0 (2026-10-08)

- Version numbers start again at 0.1.0. Builds from before the restart are listed under "before the restart".
- New Settings › What's new screen. It shows what changed in each release, and works offline.

# Earlier builds (before the restart)

These builds used the old version numbers. Their tags are kept as `legacy/v<version>`.

## 0.6.0.26 (2026-10-07)

- Every action bar now sits on the right side of the screen.
- Buttons show their key in a tooltip. Tooltips also work on buttons in the border of floats and frames.
- The dashboard info button moved to the top-right corner.
- Status icons now show under sub-orchestrators on the dashboard. They stay in one column.
- Keybinds from fleet now reach Neovim and Claude Code. Use `fleet apply-keybinds`, `fleet setup`, the menu or `fleet doctor`.
- Theme changes are saved even if another program briefly holds the file open.

## 0.6.0.23 (2026-10-07)

- There is one keybind model now. You can override keys in layers.
- Pick a theme from the fleet menu under settings. `fleet theme set` applies it to WezTerm, Neovim, Claude Code and yazi.
- Agents and sub-orchestrators show their status as coloured icons.
- The dashboard buttons moved into its frame. New agent now uses a plus. Manage is renamed to config.
- Sub-orchestrator notices use the slug as the label. A sub-orchestrator's report is no longer pushed to the main orchestrator.
- Fixed re-picking a theme, and fixed keybind overrides and the old mux migration.

## 0.6.0.22 (2026-10-07)

- Fleet has themes now. Use `fleet theme list`, `get`, `set` and `sync`. Custom themes and omarchy colours work too.
- Running fleet windows and fleetd reload the theme live.
- Fleet ships its own Neovim config.
- The installer creates a Fleet Start Menu shortcut and a Terminal profile on Windows.
- The taskbar now opens the Fleet profile window.
- Theme reload, file writes and the omarchy hook are safer.

## 0.6.0.21 (2026-10-07)

- Corner buttons and the button bar are now drawn in the border of fleetd floats.
- Button hints can show as text or as tooltips on hover. A new setting in the menu controls this.
- Fixed border buttons so they run their own action.

## 0.6.0.20 (2026-10-07)

- Buttons are now icons on the dashboard, notifications, dialogs, agent list, keybinds pane and remote machines bar.
- Every screen has the same margins around its buttons.
- Button bars are right-aligned on switch project, the agent list and the dashboard.
- Corner buttons also show on the dashboard in menu mode.
- Tab bars sit under the corner buttons.
- Switch project no longer shows the machine chip. New window is now an icon.

## 0.6.0.19 (2026-10-06)

- Fleet menus have an info icon and a close icon in the corners.
- Select and back are now icon chips.

## 0.6.0.18 (2026-10-06)

- One head chord, `alt+o`, always opens the head in voice mode.
- Button bars hide their keys. Hold `/` or press `?` to show them.
- Key capture can now record `?` and `/`. Hint keys follow your rebinds.

## 0.6.0.17 (2026-10-06)

- The fleet menu is more polished and can hide its keybinds.
- One Show keybinds switch now works for every project and the picker.
- A rebind can no longer hide a menu entry behind the reveal key.

## 0.6.0.16 (2026-10-06)

- Every Claude session gets a name. You can set the model and effort for each role in the fleet config menu.
- Dispatch to one repository starts a repo agent directly. Messages go to a target's inbox when it has one.
- Finished agents are pushed to their orchestrator. It no longer has to poll.
- The `ctrl+s` which-key popup has groups, nested submenus and Nerd Font icons. The fleet menu has Nerd Font icons too.
- Sub-orchestrators and repo agents are told to use Claude Code subagents.
- Fixed: neo-tree gets a quarter of the width when Neovim opens with Claude. A typed model alias is saved in lower case.

## 0.6.0.15 (2026-10-06)

- The fleet menu is now a tree with sections and toggles.
- Keybinds show as a grid of titled boxes. Menus, the dashboard and navigation tabs have a keybinds grid too.
- The `ctrl+s` which-key popup looks like the helix preset of nvim which-key.
- A run of digit keys shows as a range in which-key.
- Keybinds and Show log show a `›` in the settings menu, because they open a screen.

## 0.6.0.14 (2026-10-06)

- The notification center opens on the fleet you sit at, not on the remote you view.
- The tab bar scrolls so the selected tab stays visible.

## 0.6.0.13 (2026-10-06)

- New agents and sub-orchestrators start with their pane hidden.
- The head can show and hide the pane of a named agent or sub-orchestrator.
- The head has a new tool that shows which agents belong to which sub-orchestrator.

## 0.6.0.12 (2026-10-04)

- The head has a plain tell tool next to relay.
- A sub-orchestrator gets its first task only when its Claude is ready.

## 0.6.0.11 (2026-10-04)

- Fixed the head's Claude failing to start on Windows.

## 0.6.0.10 (2026-10-04)

- The head can act on projects on SSH remotes.
- You can create a new project from the project switcher, also on a remote.
- Backspace goes back one level in the fleet menu.
- Pop-ups open in the middle of the whole window. A pop-up you moved stays where it is when it resizes.
- Slow dispatches now still block the prompt.

## 0.6.0.9 (2026-10-04)

- A merge gate now guards `gh pr merge`, like the ones for commit and push.
- The other head chord restarts the head in its voice mode, in WezTerm and in the built-in multiplexer.
- The head can list projects on each machine.
- Browse runs yazi in the picker's own terminal when no pane can show it. It is hidden in the new-project form when nothing could show it.

## 0.6.0.8 (2026-10-04)

- Fleet starts faster. Repositories list without a git process per folder, panes are read once per refresh, branch pills compute in the background, and sessions restore side by side.
- Claude settings sync after the dashboard is up.
- `install.ps1` now links libghostty so fleetd can run.
- Fixed a closing connection that could break a message in flight.

## 0.6.0.7 (2026-10-04)

- You can list, stop and remove sub-orchestrators over MCP.
- Each dashboard tab shows as soon as its data is ready.
- On Linux, the installer adds the bin folder to PATH in a shell startup file.

## 0.6.0.6 (2026-10-03)

- `alt+o` toggles the head as a float on the built-in multiplexer.

## 0.6.0.5 (2026-10-03)

- Added the head orchestrator, with MCP tools that work across projects. `alt+o` and `alt+shift+o` toggle its workspace.
- Agent status now comes from Claude hooks. It is more accurate. There is a per-project setting, on by default.
- Esc on a permission prompt and compaction mid-turn no longer give a wrong status.
- A stalled agent with a prompt shows as blocked.
- Claude config writes wait for a busy file instead of overwriting it.
- Relay now types straight into a main orchestrator that runs without Neovim.

## 0.6.0.4 (2026-10-02)

- Added the aidlc engine. Dispatch can start an aidlc intent, and each project has aidlc settings in a menu screen.
- You can run main and sub-orchestrators as bare Claude, with a per-project setting.
- Idle agents that are done or failed close on their own.
- Press `e` or choose "open editor here" to open Neovim in an agent's folder.
- Reopening a stopped agent continues its conversation.
- Fixed a sub-orchestrator not knowing how its pane was started. Fixed a bare main orchestrator losing its session env after a split.

## 0.6.0.3 (2026-10-02)

- Fixed an update check that told 0.6.0.2 to update to itself.

## 0.6.0.2 (2026-10-02)

- Fleet remembers the remote machines you connected to. You can give them nicknames.
- The default keymap now follows your live keybinds.

## 0.6.0.1 (2026-10-02)

- Installers show a step progress bar, and a spinner or download meter.
- The project switcher starts on a new Open tab.

## 0.6.0 (2026-10-01)

- Fleet can now run its own built-in terminal multiplexer, so you no longer need WezTerm panes. It has tabs, splits, floating windows, mouse support, scrollback, copy mode and a tab bar at the top.
- Projects keep running in the background. Attach or switch between them with a picker, and your session comes back after a restart. `fleet daemon stop` saves it and stops it.
- You can connect to remote machines from the fleet menu. A remote project shows in your window, and you can switch back to it any time.
- New notification center. It tells you when an agent needs you, on every dashboard. It has a history tab for notices you resolved or dismissed, and the project pill shows your open notices.
- The fleet menu, folder picker and approvals now open as floating windows right where you are. New windows and panes use your terminal and Claude profile.
- Fixes: `fleet update` has more time to download and no longer reports a false failure. The agents list loads faster. The agent list no longer loses records when its file is busy. Dashboard tabs are clickable.

## 0.5.24 (2026-09-28)

- `fleet update 1.2.3` now works without the `--version` flag.

## 0.5.23 (2026-09-28)

- In an orchestrator's Claude, `ctrl+h/j/k/l` now reaches the Neovim windows first.

## 0.5.22 (2026-09-24)

- Sub-orchestrators now open with Claude alone on screen.
- `ctrl+h/j/k/l` moves you out of that Claude pane.

## 0.5.21 (2026-09-24)

- Orchestrators now run Claude inside Neovim, but show only Claude.
- Restored sub-orchestrators now continue their last conversation.

## 0.5.20 (2026-09-23)

- "Edit fleet config" now opens in the current window, not a new one.

## 0.5.19 (2026-09-23)

- Added AIDLC mode for dispatched sub-orchestrators.

## 0.5.18 (2026-09-22)

- The keybinds screen is now grouped by menu.
- "Show log" moved into the Settings menu.

## 0.5.17 (2026-09-22)

- Settings-related items now sit together under a new Settings menu item.

## 0.5.16 (2026-09-22)

- The fleet menu has a new order.
- Added an "Edit fleet config" item to the menu.

## 0.5.15 (2026-09-22)

- Dispatched sub-orchestrators now get a short, readable name made by an LLM.
- Removed the "combine windows" menu item.

## 0.5.14 (2026-09-20)

- Quitting fleet now asks first when a window holds more than one project.

## 0.5.13 (2026-09-20)

- Switching project now hides every other visible project in the window.

## 0.5.12 (2026-09-20)

- Fleet windows now use tabs.
- A take-over prompt lets you choose how to open a project.
- You can switch between projects or combine them in one window.

## 0.5.11 (2026-09-19)

- `fleet update` can now install a specific version, not just the latest.
- Added `-v` and `-l` short flags.
- `fleet update --list` shows all releases.

## 0.5.1 (2026-09-19)

- Rolled back to the 0.3.0 code.
- The dedicated WezTerm process and workspace switching from 0.4 and 0.5.0 are gone.

## 0.5.0 (2026-09-18)

- Fleet now runs in its own dedicated, isolated WezTerm process.
- Fixed a case where fleet said it removed a setup block it had not matched.

## 0.4.1 (2026-09-18)

- The workspace-switch signal now only affects the window that owns the pane.

## 0.4.0 (2026-09-18)

- Newly opened projects now get their own WezTerm workspace.
- Hidden agents move into their own project's workspace.
- After you hide or show an agent, focus returns to the dashboard.

## 0.3.0 (2026-09-18)

- In "switch project", the key case picks the current window or a new window.

## 0.2.1 (2026-09-18)

- Small internal fix to a generated theme file.

## 0.2.0 (2026-09-17)

- Agents show live working, waiting or idle activity as colored dots, with a hover tip.
- Select several agents or subs with space to hide, stop or remove them together.
- Finish an agent from the manage menu: merge, optionally push, then clean up.
- Added `fleet version` and `fleet update`, and a "switch project" choice of this window or a new one.
- Added WezTerm toast notifications for approvals and finished subs.
- Hiding, showing and removing agents is now much faster.

## 0.1.0 (2026-08-20)

- First release: a dashboard that starts agents, each in its own git worktree, with Claude or Neovim.
- Pick repositories and base branches from lists, and hide, stop, rename or remove agents.
- Projects open as a harness pane plus a dashboard in one WezTerm window, with a menu on `ctrl+space`.
- Sub-orchestrators can be dispatched, and an MCP server lets Claude drive the whole fleet.
- Per-project permissions and commit/push gates for agents.
- An installer, `fleet doctor`, a log viewer and secrets handling.
