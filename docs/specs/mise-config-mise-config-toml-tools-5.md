# Spec: ctrl+s covers all fleet-menu actions, grouped

## Problem
The ctrl+s (embedded mux prefix) which-key menu only has multiplexer controls plus one entry (`space`) that opens the fleet menu. Every fleet-menu action needs `ctrl+s space` then another key. Goal: every fleet-menu action reachable directly from ctrl+s, organised in groups.

## Scope
- Repo: **fleet-tui** only (`/home/red/repos/fleet/fleet-tui`).
- Every leaf action in the fleet menu: Main (Quit, Dashboard, List agents, Open editor, Files, Notifications; Switch project already bound), Settings > session (Save session, Remotes), configure (Edit keybinds, Show keybinds, Button hints, Theme; Fleet config items), maintenance (Rebuild dashboard, Clean up agents, Hide all agents, View logs, Update fleet, Show version, What's new), Fleet config (nvim toggles, Auto-close, Aidlc mode, Claude profile, Edit fleet config, model rows, Permissions).
- New groups mirroring menu sections, in `MuxKeys.DefaultGroups`/`DefaultIcons`, bindings in `keybinds.default.json`, labels in `WhichKey.Label`.
- Docs: README ctrl+s section, `HelpCommand` text, `RELEASE_NOTES.md` entry.

## Non-scope
- fleet-frontend (no copy change).
- Changing existing ctrl+s keys except: session group `q` moves to `s` top-level `s` leaf is removed, and tab keys (c, n, p, 1-9, &) move into a new `t` tabs group (`t c`, `t n`, `t p`, `t 1`-`t 9`, `t x`) (user decisions at Plan gate).
- Dashboard-only actions (NewAgent, AddRepository, NewProject).
- Changing fleet-menu behaviour or its own keys.

## Acceptance criteria
- AC-1: When the user presses ctrl+s, the which-key popup shall show, besides existing entries, groups for fleet actions that mirror the menu sections (agents/navigation, session, configure, maintenance), each with an icon.
- AC-2: When the user presses a group key then an action key, the mux shall perform the corresponding fleet-menu action, for every leaf action listed in Scope.
- AC-3: When a non-toggle action is chosen, the mux shall open the fleet menu floating pane directly on that action via a generic `menu <action-id>` command (equivalent to `fleet menu --action <id>`), reusing the existing handler.
- AC-4: When a toggle action (Show keybinds, Button hints, nvim toggles) is chosen, the system shall flip the value and close without leaving a menu pane open, and show a toast with the new state.
- AC-5: When the user chooses Quit fleet, the system shall quit fleet via the top-level key `ctrl+s Q`. The session group moves from `q` to `s` (`s p` switch project, `s w` save, `s R` remotes, `s d`/`s q` detach, `s r` reload); top-level `s` no longer switches project.
- AC-5b: When the user presses ctrl+s then `t` and a key, the mux shall run the tab command (new, next, prev, go to tab 1-9, close tab); the old top-level tab keys shall no longer be bound.
- AC-6: The new bindings shall be defined in `keybinds.default.json` (the Shared model) with unique keys within each group, shall not change or collide with existing ctrl+s bindings, and shall be overridable via `embedded-keys.json` like existing ones.
- AC-7: Every fleet-menu leaf action shall have a ctrl+s binding; a test shall fail when a menu action is added without one (or without an explicit exemption).
- AC-8: The which-key popup shall show a readable label for each new command (`WhichKey.Label`).
- AC-9: `fleet help` and README shall document the new groups and keys; `RELEASE_NOTES.md` shall have an entry.
- AC-10: `dotnet build`, `dotnet test Fleet.slnx` (including architecture tests: no comments in src, Shared-model rules) and `dotnet format --verify-no-changes` shall pass.

## Assumptions
- Exact letters are chosen in the Plan stage (design.md), to avoid collision with existing keys and mirror menu keys where possible.
- Toggle/headless execution and toast can reuse existing menu logic (`FleetMenus.Flip`, Value) without violating slice rules.
- The existing `menu` command already carries an action id arg (`OpenMenu(client, arg)`).
- `ctrl+s space` and ctrl+enter continue to open the full fleet menu.

## Open questions
- Exact group letters/icons: deferred to Plan.
- Whether model rows and Permissions (deeper dialogs) fit headless; they open pane directly on that row (non-toggle path). Deferred to Plan.
