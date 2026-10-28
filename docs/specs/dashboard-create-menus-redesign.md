# Spec: dashboard create menus redesign

## Problem
The dashboard menus for creating a new agent, a new sub-orchestrator and a new repository are plain: full-screen row lists, duplicated pill helpers, a picker-then-one-line-prompt flow for subs, and no way to search branches. The base-branch picker is an unfiltered list of already-fetched refs, so a branch that exists only on the remote cannot be found.

## Scope
- fleet-tui repo only, based on `main` (the four unmerged `feat/menu-*` branches are ignored).
- New agent dialog, new dedicated New sub-orchestrator dialog, Add repository dialog.
- Shared `FleetPicker` and `FleetPrompt` restyle, with an opt-in type-to-filter mode in `FleetPicker`.
- Base-branch search across local and remote branches, with a background fetch.

## Non-scope
- Changing `NewAgentHandler` / `DispatchHandler` / `AddRepositoryHandler` semantics beyond what branch search needs.
- Remotes other than `origin`.
- Dashboard tabs, other dialogs, new theme palettes.

## Acceptance criteria
Design
- AC-1: When the user opens New agent, New sub-orchestrator or Add repository, the system shall show a centered, bordered modal card with an icon title, labeled fields with inline values, key-chip pill buttons and a footer action bar.
- AC-2: The three dialogs shall build their pill buttons and field rows from one shared Ui component; the duplicated `Choice()`/`Field()` helpers shall be removed.
- AC-3: Dialog styling shall live in `Ui/`; Features shall not reference `Scheme`, `SchemeManager`, `BorderStyle`, `ShadowStyle` or `MessageBox`.

Keys
- AC-4: When a dialog is open, each field and action shall have a visible jump mnemonic shown as a key chip, and pressing it shall focus or activate that field.
- AC-5: While a dialog is open, j/k (keymap motions) and Enter shall navigate and activate as today; Esc shall cancel; Backspace on an empty field shall go back (`FleetKeys.GoesBack`, `FleetModal.Back`).
- AC-6: When the user presses Ctrl+Enter in a dialog, the system shall submit it.
- AC-7: Every new dialog view file shall be listed in `MenuBackTests` and pass it.

Shared picker / prompt
- AC-8: Where a `FleetPicker` is opened with filtering enabled, typing shall filter entries case-insensitively by substring and update the selection to the first match.
- AC-9: Where the filter matches nothing, the picker shall show an empty-state line and Enter shall do nothing.
- AC-10: `FleetPicker` and `FleetPrompt` shall use the same card styling as the dialogs; pickers without filtering enabled shall behave as before.
- AC-11: Filtering shall be enabled for the base-branch, repository and task-history pickers.

Branch search
- AC-12: When the base-branch picker opens, the system shall show cached local and remote branches immediately.
- AC-13: When the base-branch picker opens, the system shall run `git fetch --prune origin` in the background, show a spinner while it runs, and merge newly found remote branches into the list without losing the user's filter text or selection.
- AC-14: If the fetch fails or times out, the system shall show a non-blocking note (e.g. offline) and keep the cached list usable.
- AC-15: When the user chooses a remote-only branch as base, the system shall create the agent from that remote ref (`origin/<name>`).
- AC-16: After the picker is closed, a still-running fetch shall be cancelled or discarded without touching the UI.
- AC-17: The MCP `list_branches` tool shall keep working unchanged (no forced fetch).

New sub-orchestrator
- AC-18: When the user opens New sub-orchestrator, the system shall show a dedicated dialog with a multi-line task field and a list of past tasks.
- AC-19: In the task field, Enter shall insert a newline and Ctrl+Enter shall dispatch; selecting a past task shall fill the field.
- AC-20: If the task is empty or whitespace, the system shall not dispatch and shall show an inline error.

Add repository
- AC-21: When the user submits Add repository with an empty Name (or an empty URL while cloning), the system shall show an inline error and not close the dialog.
- AC-22: Add repository shall keep the clone-from-URL / new-repository toggle, known-URL suggestions and Branch default of "main".

Quality
- AC-23: After any `await` in dashboard callbacks, modals shall open via `FleetAsync.OnUi`.
- AC-24: `dotnet build Fleet.slnx`, `dotnet test Fleet.slnx --nologo -v minimal` and `dotnet format Fleet.slnx --verify-no-changes` shall pass; src shall contain no comments.
- AC-25: Each changed behaviour shall have a test (filter logic, branch merging, validation, key handling as pure logic or source-checks per repo style).
- AC-26: A RELEASE_NOTES entry shall describe the change.

## Assumptions
- A1: Only `origin` matters for remote branches (matches current lister).
- A2: Fetch timeout of 15 s is acceptable.
- A3: Nerd Font icons are available as elsewhere in the UI (`FleetIcons`).
- A4: Jump mnemonic letters are chosen per dialog and honor the keymap where one exists.
- A5: Walking skeleton = New agent dialog with the shared card/pill component and the filterable base-branch picker (covers AC-1..AC-6, AC-8..AC-17).

## Open questions
- OQ-1 (deferred to Plan): how `NewAgentCommand.Base` resolves to the worktree start point in `NewAgentHandler`; AC-15 depends on it.
- OQ-2 (deferred to Plan): exact mnemonic letters and card sizes.
