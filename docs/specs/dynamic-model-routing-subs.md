# Spec: dynamic model routing for sub-orchestrators and repo agents

Repository: fleet-tui only. See discover.md.

## Problem
The model and effort for a sub-orchestrator (default `sonnet/medium`) and for repo agents (default `inherit`) are static per project and role. A trivial task and a hard cross-repo feature get the same model. A cheap Haiku call can pick a fitting model and effort per task.

## Scope
- `IModelRouter` port plus `ClaudeModelRouter` (`claude -p --model haiku`), wired only in `Cli/Composition`.
- Pure parser/validator of the router output in `Shared`.
- `auto` value for the Sub and Agent model settings, with settings-menu rows.
- Routing in `DispatchHandler` (subs, direct repo-agent dispatch) and `NewAgentHandler` (`new_agent`).
- Persist chosen model and effort on `AgentRecord`; use them on restore/reopen.
- Router runs in parallel with the slug namer.

## Non-scope
- fleet-frontend.
- Direct Messages API, API-key handling.
- Explicit `model` parameter on `dispatch` / `new_agent` (deferred).
- Routing the Main or Head roles.
- Pinning an exact "Haiku 5.5" model ID (router uses alias `haiku`).
- Re-routing on restore.

## Acceptance criteria
- AC-1: When the Sub model setting is `auto` and a sub-orchestrator is dispatched, the system shall ask the router for a model and effort and launch the sub with the validated result as `--model`/`--effort`.
- AC-2: When the Agent model setting is `auto` and a repo agent is started via `new_agent` or direct dispatch, the system shall do the same for that agent.
- AC-3: When the Sub or Agent model setting is not `auto`, the system shall not call the router and shall behave exactly as before (defaults `sonnet/medium` for subs, `inherit` for agents).
- AC-4: When the router errors, exits non-zero, exceeds 8 s, or returns output that fails validation, the system shall fall back to the static setting for that role, write a line to the fleet log, and still complete the dispatch.
- AC-5: The router shall only accept a model among `haiku`, `sonnet`, `opus`, `fable` and an effort among `low`, `medium`, `high`, `xhigh`, `max`; any other value shall count as invalid output (AC-4).
- AC-6: The router shall receive the task text, the Ai-DLC profile, the research flag and the repository name, and shall be invoked as `claude -p ... --model haiku --output-format text` through the same executable resolution as `ClaudeSlugNamer`.
- AC-7: When a sub or agent was started with a routed model, the system shall store the model and effort on its `AgentRecord`.
- AC-8: When a stored agent with a persisted model/effort is restored or reopened, the system shall launch it with the persisted values and shall not call the router.
- AC-9: When an `AgentRecord` written before this change (no model/effort) is loaded, the system shall derive the model from settings as before.
- AC-10: When dispatch needs both a slug and a routed model, the system shall run both calls concurrently, so added latency is at most the slower call.
- AC-11: The settings menu shall offer `auto` for the Sub and Agent model rows, and the value shall round-trip through the settings store.
- AC-12: The change shall pass `dotnet build Fleet.slnx`, `dotnet test Fleet.slnx --nologo -v minimal` (including architecture tests: no comments in `src`, layering, source-generated JSON) and `dotnet format Fleet.slnx --verify-no-changes`.

## Assumptions
1. Router uses the alias `haiku`, not a pinned ID.
2. Router input is task text, profile, research flag, repository; output is strictly validated.
3. `claude -p` uses the user's existing Claude Code auth; no new secrets.
4. Existing tests asserting `--model sonnet --effort medium` stay valid because `auto` is opt-in.
5. Routing for `fable` and `max` effort is allowed by decision (cost is the user's call via opting in).

## Open questions
- Deferred: explicit `model` override on `dispatch` / `new_agent`.
- Deferred: pinned Haiku 5.5 model ID; verify against installed Claude Code later.
- Deferred: one merged Haiku call (slug + model) as a later optimisation.
