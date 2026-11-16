> Copied verbatim from `../fleet-multiharness-interop-improvements/specs/messaging.md` on 2026-11-13. **Approved by the user on 2026-11-11** (Specify interview skipped per TASK.md). Relative paths below (`map.md`, `reports/…`) refer to that orchestration folder.

# Spec: messaging, notify and status

Repository: fleet-tui. Map: `map.md`, ticket `02-messaging-scope` (closed). Stage: Specify. Status: **approved by user 2026-11-11**.
Build starts only after `01-stack-merged` closes (multi-harness stack on `origin/main`); re-check every file reference below against `origin/main` then.

## Problem
Fleet's messaging between orchestrators and agents is unreliable, more so with mixed harnesses:
- Typed messages and owner notices overwrite `.fleet/instruction.md` (`Features/Agents/TellAgent/TellAgentHandler.cs:54-60`). A second message before the first is read is lost; and `wait_for_agent` judges staleness by that file's mtime (`Features/Orchestrations/WaitForAgent/WaitForAgentHandler.cs:87-99`), so a child's notice makes a sub's own `done` look stale. A tell sent via `SendMessage` never touches the file, so a stale `done` still counts.
- Status changes only through `report`. Subs that never report stay `working` forever; `remove_sub` refused 8 times in fleet.log (4 subs). A session that ends leaves the record `working`.
- `wait_for_agent` blocks one MCP call for 300 s (max 3600 s), but codex and pi abort tool calls at 60 s. Fleet's stdio MCP server (`Platform/Mcp/StdioMcpServer.cs:16-36,58-63`) handles calls one at a time and ignores `notifications/cancelled`, so an abandoned wait blocks every later fleet call from that harness.
- The `SendMessage` address is the last hook's socket, never checked for liveness, kept 24 h, not cleared on stop/remove (`AgentStatusRules.cs:40-47`, `Platform/Hooks/HookIo.cs:46-49`).
- Idle detection hashes the whole pane with a fixed 120 s window (`Shared/Status/PaneQuiet.cs:10`): clocks, spinners and statuslines prevent idle.
- Approval popups open over the caller's pane; when nobody views that project the call times out after 120 s with a misleading "Timed out waiting for the dashboard to answer" (`Platform/Approvals/FileApprovalChannel.cs:19,76`), and an approval answered after codex/pi gave up still runs.
- A top-level non-Claude sub's `done`/`failed` is only polled by main; an unknown caller is treated as Claude; typed prompts can be double-typed, the nvim pump has no echo check, and `new_agent` can claim a task was delivered when it was not.

Evidence: `reports/03-owner-inbox-design.md`, `04-wait-timeouts.md`, `05-idle-detection.md`, `06-stale-status-and-timeouts.md`, `improvement-candidates.md` §D.

## Scope
1. Instruction time on the agent record; owner notices in an append-only notices file.
2. Status lifecycle: reset on tell, `failed` on exit without report, stop option on `remove_sub`, report-at-gates kickoff text.
3. `wait_for_agent` default and per-harness cap; explicit tool timeouts in harness configs.
4. Concurrent, cancellable MCP server with progress notifications.
5. Liveness of `SendMessage` addresses.
6. Configurable idle window and volatile-line filtering.
7. Approval popup visibility, fail-fast, wording and deadline.
8. Push top-level sub `done`/`failed` to main.
9. Unknown caller handling; `OrchestrationText` wording.
10. Typed-prompt reliability.

## Non-scope
- Native per-harness idle/status hooks (pi extension, opencode plugin, codex hooks) — harness-parity spec (ticket `12-harness-parity-scope`).
- A file-per-message inbox or an MCP inbox tool (03 options B/C) — only if lost tells are seen after this ships.
- Claude-to-Claude `SendMessage` delivery itself (unchanged apart from AC-14/15).
- Landing the multi-harness stack (owned by the multi-harness-interop sub).

## Acceptance criteria

### Instructions and notices
- AC-1. When `tell_agent` delivers a message to an agent by any route (typed or `SendMessage`), the system shall set `InstructedAt` on that agent's record to the delivery time, and the store shall round-trip the field (records without it load with no value).
- AC-2. When `wait_for_agent` decides whether an agent has finished, the system shall treat a `done`/`failed` report as current only if its `ReportedAt` is later than the record's `InstructedAt` (or `InstructedAt` is unset), and shall not read the mtime of `.fleet/instruction.md`.
- AC-3. When fleet sends an owner notice (report notice, idle notice, exit notice), the system shall append one timestamped line to `<worktree>/.fleet/notices.md` without changing `.fleet/instruction.md`, and type a prompt telling the receiver to read the new lines at the end of `.fleet/notices.md`.
- AC-4. When a child's notice reaches a sub that has already reported `done` after its last instruction, `wait_for_agent` on that sub shall still return it as finished.

### Status lifecycle
- AC-5. When `tell_agent` successfully delivers to an agent whose status is `done` or `failed`, the system shall set its status to `working`.
- AC-6. When an agent's session ends (SessionEnd hook) or its pane disappears while its record is `working`, the system shall set the status to `failed` with summary "session ended without a report" and send the owner the same notice as a reported `failed`.
- AC-7. When `remove_sub` is called with `stop: true` on a sub that is `working`, the system shall stop it and then remove it in one call; without `stop`, the existing refusal for a working sub with an open pane shall remain.
- AC-8. When fleet writes the kickoff/instruction text for a sub-orchestrator, it shall state that the sub must call `report` with `done` or `failed` at the end and `working` with a summary such as "waiting on user" at each human gate.

### Waiting and the MCP server
- AC-9. When `wait_for_agent` is called without a timeout, the system shall wait at most 45 s; when called with a larger timeout, it shall clamp it to the calling harness profile's cap (codex and pi 55 s; claude and others per profile, never above the client's documented limit minus a margin), and say in the result that it was clamped.
- AC-10. When fleet writes a harness's MCP config (claude `.mcp.json`, codex `.codex/config.toml`, opencode, pi), it shall set that harness's per-server tool timeout explicitly where the format supports it, without changing claude's generated files beyond that field (claude golden tests updated deliberately).
- AC-11. When the MCP server receives a `tools/call` while another call is running, the system shall process it concurrently, and `ping` shall be answered while a long call runs; stdout writes shall never interleave.
- AC-12. When the MCP server receives `notifications/cancelled` for a running request, the system shall cancel that request's work and send no result for it.
- AC-13. When a `tools/call` carries `_meta.progressToken` and runs longer than 20 s, the system shall send `notifications/progress` for it at least every 30 s until it completes.

### SendMessage addresses
- AC-14. When fleet would return a `SendMessage` address for an agent, the system shall first check the recorded messaging socket exists; if not, it shall fall back to typed delivery and not return the address.
- AC-15. When an agent is stopped or removed, the system shall clear its stored hook state (messaging socket and live status) so no later call returns its old address.

### Idle detection
- AC-16. The system shall read the idle window from one global setting (default 120 s), let a harness profile override the default, and use that value in the dashboard, `list_agents` and `wait_for_agent`.
- AC-17. When fleet hashes a pane for idle detection, it shall ignore lines matching the harness profile's volatile-line patterns (clocks, token/cost counters, spinners) and shall not reset the idle timer on a pane resize alone.
- AC-18. When a pane shows only a changing clock or spinner line and nothing else changes for longer than the idle window, the system shall report the agent idle (test with recorded pane captures).

### Approvals
- AC-19. While an approval is pending, the system shall show a desktop notification and a dashboard alerts-strip row naming the caller, the project and the action.
- AC-20. When an approval is requested and no attached client is showing the caller's project, the system shall fail the call at once with "approval needed in project <X>; open it to answer" instead of waiting.
- AC-21. When an approval times out, the error shall say "No one answered the approval prompt (shown over <caller> in <project>) within <N> s" (no mention of the dashboard).
- AC-22. The system shall cap an approval wait at the calling harness's client limit, and when an approval is answered after that deadline, it shall not run the action.

### Top-level subs, callers and wording
- AC-23. When a top-level sub-orchestrator running a non-Claude harness reports `done` or `failed`, the system shall deliver a notice to the main orchestrator (AC-3 path) as well as recording it.
- AC-24. When a fleet MCP call comes from a caller that is unknown or removed, the system shall treat it as non-Claude (typed delivery only, no `SendMessage` address).
- AC-25. The text in `Shared/Orchestrations/OrchestrationText.cs` shall describe the actual mechanism (writes the notices file and types a read prompt), not "types a [fleet] line".

### Typed prompts
- AC-26. When fleet types a prompt and the echo check does not see it, the system shall retry at most once and only if the prompt text is not already present in the pane, so a prompt is never submitted twice.
- AC-27. When the nvim host relays a typed prompt, the system shall apply the same echo check as the bare pane path.
- AC-28. When `new_agent` or `dispatch` cannot confirm delivery of the first task (no echo, no MCP ready marker), the result shall say the task was written but not confirmed, instead of "gave it its first task".

### Repo rules
- AC-29. The system shall keep the repo rules: no comments in `src`, slice/Platform dependency rules, AOT-safe source-generated JSON, MCP path never uses `Console`; `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` pass (with the NU1903 fix on main, without `NuGetAudit=false`).

## Decisions (user, ticket 02)
All of D in; inbox = option A (InstructedAt + notices.md); exit without report = `failed` + reason (no new status); `wait_for_agent` 45 s default with per-harness cap, progress not relied on to extend waits; concurrent cancellable MCP server in this spec; idle window = one global setting with per-harness defaults; native idle hooks to harness parity.

## Assumptions
- The multi-harness stack (harness profiles, typed delivery, `WaitForAgentHandler`, `PaneQuiet`, `OwnerNotices`) merges largely as at stack tip `1d83abed`; line references are from there.
- `McpActions` and the JSON agent store can be made safe for concurrent calls (store already uses file locks; shared state in `McpActions` to be checked in Plan).
- Desktop notifications and the alerts strip (`Features/Notifications`) can carry approval rows without new infrastructure.
- The daemon can tell which projects attached clients are showing (`ListWorkspacesAsync` `ShownHere`/`InWindow`).

## Open questions
1. **Deferred to Build (live checks):** opencode 1.4.6 tool-call timeout (sets its cap in AC-9/10); idle screens of codex, pi and opencode (sets volatile patterns in AC-17); whether progress resets codex/opencode timers (informational; AC-9 does not depend on it).
2. **Deferred to Plan:** unit split and order (likely: MCP server concurrency first, as AC-11-13 underpin waits and approvals), and how this spec's units avoid file overlap with the orchestration and harness-parity specs (shared `McpActions.cs`, `OrchestrationText.cs`).

## Amendments (user decisions during Build)
- 2026-11-16, AC-11 narrowed: read-only tools (`wait_for_agent`, `list_agents`, `agent_status`, `list_subs`, `log_tail`, other listing/status tools; explicit allow-list) run concurrently with any call; mutating tools stay one at a time per MCP server, and a cancelled call releases that gate. `ping`/`initialize`/`tools/list` are always answered at once. Reason: full concurrency exposed in-process races between mutating calls (two tells on one pane, stale whole-record writes over `InstructedAt`, same-slug dispatches). Known remaining races: head relay check-then-type, `pane.mark` read-modify-write.
- 2026-11-16, AC-6 narrowed (user): a pane fleet kills on purpose (stop_agent, quit project, daemon stop/restart, open_agent replacing a stale pane) is not "a session that ended without a report": fleet marks the agent as closing before the kill, and neither the SessionEnd hook path nor the pane-gone path fails it. Only unexpected ends become `failed`.
- 2026-11-16, AC-14 narrowed (user): on Windows the SendMessage address is a named pipe, which has no cheap existence check; it is handed out unchecked there. Stop/remove still clears it (AC-15). On Unix the check is that the socket file exists.
- 2026-11-16, AC-17 narrowed (user): "shall not reset the idle timer on a pane resize alone" covers a pure reflow (the same text re-wrapped at another width, borders and padding that follow the width). A resize that also changes what is shown (a path cut at another place, a sidebar, lines scrolling off, a height change) may restart the idle timer once, so idle is reported up to one window late, never early. Carrying the pane size from the mux is a follow-up.
