# Spec: multi-harness-interop

Repository: fleet-tui. Stage: Specify. Status: awaiting user approval.

## Problem
Fleet only runs Claude in practice. Harnesses are the closed set `claude`, `nvim`, `orchestrator` (`Shared/Constants/AgentHarness.cs`), and everything else is Claude-wired: MCP config (`.mcp.json`, `.claude/settings.local.json`), hooks and status, skills, `CLAUDE.md`, and cross-session `SendMessage`. The user wants to run other popular harnesses (codex, opencode, pi, later copilot) in fleet, mixed freely: a Claude main orchestrator with a pi or opencode sub-orchestrator or worker, all still able to talk to each other.

## Scope
1. A `HarnessProfile` registry replacing the name-switch in `AgentHarness`: per harness, launch command, resume, env, instruction file, MCP config writer, optional hooks/skills/permission writers, capabilities.
2. Concrete profiles: claude (refactored, behaviour unchanged), opencode (walking skeleton), codex, pi.
3. Mixed-harness messaging: fleet-side delivery when caller or target is not claude; fleet-native idle/done notification.
4. Any role (main orchestrator, sub-orchestrator, worker) can use any harness; main orchestrator defaults to claude.
5. Harness selection UI/MCP (`new_agent`, `change_harness`, pickers, settings) lists registered profiles; unknown names are rejected.
6. Nvim host becomes an independent option from the harness.
7. Setup/`fleet doctor` check that the CLI of each used harness is installed.
8. Docs: README "Agents", docs/DESIGN.md section, per-harness capability table.

## Non-scope
- Copilot CLI (later task); gemini, aider, amp, cursor.
- ACP, A2A, opencode server API or any structured-stream integration.
- Native per-harness hook/plugin integrations for status (optional extras only if trivially available; not required).
- Sandbox, cost ledger, other roadmap items.
- Rewriting claude-to-claude messaging.
- Changing the nvim-hosted claude experience.

## Acceptance criteria
- AC-1. When a harness name is registered, the system shall resolve it through a `HarnessProfile` registry (no per-name switch outside the registry) exposing: launch command, resume command, spawn env, instruction file name, and capability flags (mcp, hooks, skills, permissions, nativeMessaging).
- AC-2. When `new_agent` or `change_harness` receives a name that is not registered, the system shall fail with an error listing the valid harnesses and shall not change stored state; when stored state holds an unknown value, load shall treat it as `nvim` (legacy behaviour).
- AC-3. When the harness is `claude`, the system shall produce byte-identical launch commands, env, `.mcp.json`, `.claude/settings.local.json` and skills as before the change (existing tests stay green).
- AC-4. When an agent or orchestrator is opened with a non-claude harness, the system shall write that harness's project-local MCP config registering the fleet MCP server with the correct `--caller`, so the fleet tools (including `report`, `tell_agent`) are available to it.
- AC-5. When an orchestrator or agent runs a non-claude harness, the system shall write the instructions to that profile's instruction file (`AGENTS.md` for codex/opencode/pi) with agent-neutral wording and use a kickoff prompt naming that file.
- AC-6. When a non-claude agent is started or sent a follow-up, the system shall deliver the prompt by writing `.fleet/instruction.md` and typing the standard read-instruction prompt into the pane, and the existing report rule shall apply.
- AC-7. When `tell_agent` is called and either the calling session or the target agent is not claude, the system shall deliver fleet-side (typed) and shall not return a `SendMessage` address for the caller to send; when both are claude, behaviour is unchanged.
- AC-8. When a non-claude agent calls `report` with `done` or `failed`, the system shall record the status, notify the desktop, and make it available to its owner (orchestrator) through a fleet-native notification that works without Claude's `notify_when_idle`.
- AC-9. When a non-claude agent has no recent pane output beyond the configured idle window and has not reported done, the system shall show it as idle in `list_agents` and the dashboard.
- AC-10. When a sub-orchestrator is dispatched with a non-claude harness, the system shall start it in its orchestration folder with the profile's instruction file and MCP config, and its stage/gate behaviour shall be the same as for claude.
- AC-11. When the user selects a harness in the new-agent view, the settings, or dispatch parameters, the system shall list all registered profiles and persist the choice per agent; the harness and the "host in nvim" option shall be stored and chosen independently.
- AC-12. When a profile lacks a capability (hooks, skills, permission policy), the system shall skip that step without error and `fleet doctor`/docs shall state the degradation; skills shall be installed to the harness's skill directory where it supports `SKILL.md`.
- AC-13. When `fleet doctor` or setup runs, the system shall report for each harness in use whether its CLI is on PATH.
- AC-14. When `fleet attach`/rebuild reopens a non-claude agent, the system shall resume it with the profile's resume command if it has one, else start fresh.
- AC-15. The system shall keep the repo rules: no comments in `src`, slice/Platform dependency rules, AOT-safe JSON, MCP path never uses stdout; `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` pass.
- AC-16. When a mixed team (claude main, opencode sub or worker) runs, an end-to-end test with a fake mux and fake harness shall show: dispatch, instruction delivery, tell_agent in both directions, report, and orchestrator notification.

## Decisions (from interview)
Pty-based profiles; ACP out of scope; opencode skeleton; codex and pi follow; copilot deferred; best-effort parity; reject unknown names; status via report plus pane activity; per-profile instruction file; independent nvim host; follow the existing launch path for ssh/Windows; PRs go to Forgejo.

## Assumptions
- opencode, codex and pi each support a project-local MCP stdio config and read `AGENTS.md`; each accepts typed text in its TUI. To be confirmed by `reports/research/harness-clis.md` (research still running) before Plan.
- "pi" is `earendil-works/pi` (`@earendil-works/pi-coding-agent`, binary `pi`), per reports/research/harness-clis.md.
- Research result (secondary, summarized fetch): opencode and codex have project-local MCP config and read AGENTS.md; codex's project config loads only if the project is trusted. pi's MCP support is UNVERIFIED (no config format found; `builtin:mcp` extension mentioned). If pi has no MCP, AC-4 cannot be met for pi and the pi profile moves to an extension-based bridge or is dropped from this task (decision at Plan).
- No harness documents accepting typed/pasted text plus Enter in a pty; AC-6 relies on this and must be proven empirically in the opencode skeleton.
- Idle detection reuses the existing pane-activity mechanism (`PaneActivityTests`).

## Open questions
1. Per-harness facts: mostly answered by reports/research/harness-clis.md (read it). Still deferred to Plan: pi MCP path, codex `-c mcp_servers` quoting vs `.codex/config.toml` trust, pty typed-input behaviour per harness.
2. Fleet-native notification to a non-claude orchestrator: how a non-claude orchestrator receives "agent done" (typed into its pane vs a `wait_for_agent` MCP tool vs both). Deferred to Plan (design.md); recommendation: typed message plus a new MCP tool for polling-free waiting.
3. Whether codex hooks/notify can feed status cheaply: deferred, optional.

## Delivered vs not delivered

Added at delivery (2026-11-11). The text above is the spec as approved; this section says what the code does.

Met:
- AC-1, AC-2, AC-3: `HarnessProfile`/`HarnessRegistry`; unknown names refused by `Require`; Claude commands, env and config unchanged (golden tests).
- AC-4, AC-5: opencode (`opencode.json`), codex (`.codex/config.toml`) and pi (`.pi/mcp.json`) get the fleet MCP server with the right `--caller`; orchestration folders get `AGENTS.md` and a kickoff naming it. Worker worktrees get no `AGENTS.md` from fleet (the repository owns that file, as with `CLAUDE.md`); their instructions come through `.fleet/instruction.md`.
- AC-6, AC-7, AC-8, AC-9: typed delivery, typed `tell_agent` when either side is not Claude, typed `[fleet]` owner notices plus the `wait_for_agent` tool, idle after 120 s of a quiet pane.
- AC-11: `Harness` and `HostInNvim` stored and chosen separately; pickers and the **Agent harness** setting list the registry.
- AC-12: steps a profile lacks are skipped; `fleet doctor` and the README table state them; skills go to `.agents/skills`.
- AC-13, AC-14: doctor and setup report each harness CLI; reopen uses the profile's resume command.
- AC-15: repo rules hold; build, tests and format pass (with `NuGetAudit=false` while the ILCompiler advisory NU1903 blocks restore; not part of this work).
- AC-16: `tests/Fleet.Tests/EndToEnd/MixedTeamE2ETests.cs`.

AC-10, delivered by the integration unit (u7), with these deviations:
- A sub on opencode, codex or pi is first started outside nvim, whatever "Sub-orchestrators in nvim" says: the first typed prompt is checked against the pane, and the nvim pump cannot do that. On reopen it follows the setting.
- New: fleet types the first prompt of these harnesses again until the pane shows it (`HarnessProfile.InputRetry`), because the real opencode dropped a kickoff typed while it was starting.
- Only the `dispatch` MCP tool takes a `harness`. The `,` hook and the dashboard's New sub-orchestrator card start Claude subs.
- "Stage and gate behaviour the same" holds by construction (same instruction text, harness-neutral `report` and intent record); it was not observed with a real non-Claude model.
- `list_agents` lines now name the harness for every agent, Claude ones included.

Not proven on a real binary (details: docs/DESIGN.md, "Harness profiles and mixed teams"):
- A model turn of an opencode sub, and any model turn of codex or pi: no usable login in the test sandbox. An opencode worker reading the typed task and calling `report` is proven.
- A typed prompt arriving in codex; that pi's first prompt was typed exactly once.
- Resume of a sub-orchestrator; a non-Claude sub reopened in nvim.
- Two real harnesses talking to each other. The mixed team is proven with a fake mux and scripted harnesses only.
- A `wait_for_agent` call that blocks for minutes in the MCP client of opencode, codex or pi.
- Idle on a real TUI: the pane-quiet window is a fixed 120 s, and a screen that redraws a spinner or clock reads as working.
- The ctrl+u fleet sends before it types a prompt again.

Known weakness: a notice overwrites the owner's `.fleet/instruction.md`, so a late notice from a child can make an owner's own `done` look stale to `wait_for_agent`.

Out of scope, not delivered:
- A main orchestrator on another harness (scope item 4 says "any role"). The main pane, the head orchestrator and the dispatch hook stay Claude; it needs its own setting and unit.
- Removing codex/pi trust entries when an agent is removed; a setting for the idle window; model routing for other harnesses.

Settled since the spec: the pi assumptions above (pi is `@earendil-works/pi-coding-agent`, and it has built-in MCP with a project-local `.pi/mcp.json`); open question 2 (both a typed line and `wait_for_agent`); open question 3 (codex hooks are not used).

Open decision for the user: trusting a folder for pi lets pi load that repository's own `.pi/extensions` (code) without asking.
