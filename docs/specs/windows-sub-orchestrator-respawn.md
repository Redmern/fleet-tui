# Spec: windows-sub-orchestrator-respawn

Stage: partly delivered. u1 (fix/sub-removal-oneway: AC-1, AC-2, AC-5, AC-6, AC-7) ships in 0.7.25; u2 (AC-8, daemon snapshot restore) and u3 (AC-3, AC-4, Windows process-tree kill) are not done yet. Evidence: code reading only (see discover.md); the Windows machine is no longer available, so the root cause is **not reproduced**. Fix targets every code path that can resurrect a removed sub-orchestrator.

## Problem
On Windows (plain cmd terminal, default fleet nvim config), a sub-orchestrator removed from the dashboard Subs tab kept coming back and could not be thrown away. Code shows several ways this can happen: a failed folder delete re-saves the record; Windows pane close terminates only the root process (nvim), leaving Claude/node alive and holding the folder; the agent store `Save` is a blind upsert that stale writers use to revive a removed record; `Remove` can silently do nothing; `Open && folder exists` makes restore respawn the sub.

## Scope
- Dashboard Subs-tab removal and `remove_sub`/`RemoveAgentHandler` (sub-orchestrator folders).
- Windows pane teardown (process tree).
- `JsonAgentStore` Save/Remove semantics and its stale-record writers (RestoreSession, QuitProject, Report, HideAgent, OpenAgent, Rename).
- User-visible message when a folder cannot be deleted.

## Non-scope
- UI redesign of the Subs tab; repo-agent (worktree) removal behaviour beyond sharing code paths; Linux/macOS pane teardown changes; fleet-nvim config.

## Acceptance criteria
- AC-1: When removal of a sub-orchestrator from the dashboard or `remove_sub` fails to delete its folder, the system shall NOT re-save its record; the sub shall disappear from the list and shall not be restored on any later start.
- AC-2: When the folder could not be deleted, the system shall tell the user (toast/note) the folder path, that it was kept, and that it can be deleted manually.
- AC-3: When a pane is closed or killed on Windows, the system shall terminate the pane's whole process tree (nvim, Claude, node, MCP children), e.g. via a Job Object with kill-on-close.
- AC-4: When terminating a process fails (including access denied), the system shall log it and include it in the removal result instead of ignoring it.
- AC-5: When a record has been removed, the agent store shall not recreate it through a later `Save` of a stale record by a non-creating caller (restore, quit, report, hide, open, rename); only creating callers (dispatch, new agent) may add records.
- AC-6: When `JsonAgentStore.Remove` cannot read or lock the session file, the system shall report failure to the caller rather than silently succeed, and the caller shall not claim the agent is removed.
- AC-7: When fleet starts or restores a session, the system shall NOT respawn a sub-orchestrator whose removal was requested, even if its folder still exists.
- AC-8: When the daemon restores its pane snapshot, the system shall NOT relaunch a pane whose agent record no longer exists.
- AC-9: Each of AC-1, 5, 6, 7 shall be covered by a test that fails on current `main`; AC-3 shall be covered by a Windows e2e check in `scripts/e2e/windows.ps1`.

## Assumptions
- Root cause not reproduced; the fix is defensive across all candidate paths. Windows verification is deferred to the e2e script / user retest.
- A "removal requested" marker (tombstone) may be used for AC-7; design decides the mechanism.
- Default `SubOrchestratorsInNvim = true` is the affected setup.

## Open questions
- None blocking. Deferred: user retest on Windows after release (no machine access now).
