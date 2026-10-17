# Spec — fleet permissions UX + nvim

Status: approved

## Problem
Settings > Fleet config > Permissions (`EditSettingsView`) needs click -> picker (-> second picker for channel) per change; a `channel` chip is unexplained; defaults ask for too much; no way to edit the settings JSON in an editor.

## Scope
- fleet-tui only: `Features/Menu/EditSettings/*`, `Shared/Settings/SettingsDefaults.cs`, composition callbacks in `Cli/Commands/MenuCommand.cs` and `Cli/Composition/DashboardWiring.cs`, README line on defaults, tests.

## Non-scope
- No change to `SettingsFile` schema or the policy+channel model (migration-free).
- No editors other than nvim.
- No change to RELEASE_NOTES (release time only).
- No change to how Claude rules are planned (`ClaudePermissionPlanner`).

## Acceptance criteria
- AC-1: When the user presses Enter or clicks a tool row, the system shall advance its policy allow -> ask -> forbid -> allow in place, persist it, and open no picker.
- AC-2: When the user presses `a`, `s` or `f` on a selected tool row, the system shall set that row to allow, ask or forbid respectively and persist it.
- AC-3: When a row's policy becomes ask, the system shall not open a channel picker; the channel stays unchanged (default `both`).
- AC-4: The screen shall show tabs "All", "Fleet dialog" and "Claude"; Tab / Shift+Tab / left / right switch tabs.
- AC-5: While the "Fleet dialog" tab is active, the system shall list only rows with policy ask and channel fleet-dialog or both; on the "Claude" tab, only ask rows with channel claude or both; "All" lists every row.
- AC-6: When the user presses `c` on a tool row with policy ask, the system shall cycle its channel both -> fleet dialog -> claude and persist it; on a non-ask row it shall show "The channel only matters when the policy is ask."
- AC-7: The action bar shall not show a bare "channel" chip; it shall show a labelled chip ("ask where") and the status line shall explain the channel on focus of an ask row.
- AC-8: Git-gate rows (commit/push/merge) and the status-hooks switch shall also change in a single action (Enter cycles auto/ask/no; hooks toggles) with no picker.
- AC-9: The default policy shall be forbid for RemoveRepository and allow for every other tool and git gate (commit, push, merge).
- AC-10: When saved, settings that equal the new defaults shall not be written (existing diff-against-defaults behaviour); a file customised under old defaults keeps its explicit values.
- AC-11: README default-permissions text shall describe the new defaults.
- AC-12: When the user presses `e` or clicks "open in nvim", the system shall ensure the settings JSON exists, spawn a multiplexer pane running nvim on it (as `EditFleetConfig` does), and show an error status if the multiplexer is unsupported.
- AC-13: When that nvim pane exits, the system shall reload the settings from disk, refill the list, and (dashboard only) call the Claude resync; an unparsable file shall leave the previous settings and show a status message.
- AC-14: The open-in-nvim spawn shall be a callback passed into `EditSettingsView.Show` (feature slice does not reference Platform).

## Assumptions
- A1: "Red allow" in the interview read as "rest allow" (confirmed by user: everything else allow, including git push and commit).
- A2: Because tabs are views over the unchanged model, tools with policy allow/forbid appear only on "All".
- A3: "Open in nvim" detects nvim exit via the spawned pane's process completion (as `TitledCommand` waits); if the mux driver cannot report exit, fall back to reload on screen refocus (to verify in Plan).
- A4: Existing users with explicitly saved values keep them; only unset tools pick up looser defaults.

## Open questions
- None blocking. Deferred: per-channel independent policies (rejected for now, schema change).
