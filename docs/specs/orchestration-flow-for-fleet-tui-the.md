> Approved by the user on 2026-11-12. Copied verbatim from fleet-multiharness-interop-improvements/specs/orchestration.md. Specify interview skipped per TASK.md.

# Spec: orchestration flow

Repository: fleet-tui. Map: `map.md`, tickets `07-orchestration-scope`, `08-stale-main`, `09-new-agent-base`, `10-evidence-report`, `11-forgejo-delivery` (all closed). Stage: Specify. Status: **approved by user 2026-11-12**.
Build starts only after `01-stack-merged` closes (multi-harness stack on `origin/main`); re-check every file reference below (from stack tip `1d83abed`, paths under `src/Fleet`) against `origin/main` then.

## Problem
Fleet's Ai-DLC flow has frictions seen repeatedly across past orchestrations (learnings in 6+ task folders):
- **Stale `main`.** In the bare layout nothing ever moves `refs/heads/main` (local `main` is 256 commits behind `origin/main` today). `pull_repository` only fast-forwards a `main/` worktree, which doesn't exist (`Features/Repositories/PullRepository/PullRepositoryHandler.cs:16-29`). The Discover and Review stage text never says to fetch or what "base" is (`Shared/Aidlc/ProcessText.cs:85-88,97`). Orchestrators diff against stale `main` and mislead themselves.
- **No base on `new_agent`.** The MCP tool always passes `repo.DefaultBranch` (`Cli/Composition/McpActions.cs:267-276`); stacked units need a manual merge. A branch that exists only as `origin/<branch>` is silently cut fresh from `origin/main` (`Features/Agents/NewAgent/NewAgentHandler.cs:272-285`).
- **Reports carry no evidence.** `report` takes status + summary only (`Ports/Agents/Models/AgentRecord.cs`, `Features/Orchestrations/ReportStatus/ReportStatusHandler.cs`); orchestrators re-ask every agent for path, SHA and test output.
- **Wrong profile.** Research and bugfix tasks ran as `feature` although those profiles exist (`Shared/Aidlc/Enums/Profile.cs`).
- **Release surprises.** Version and release-notes headings are decided late (placeholder `0.7.30` in the multi-harness stack).
- **Gate drift.** Workers commit after reporting done and start before the skeleton gate is approved; nobody notices.
- **Overlap and approvals.** Parallel units touching the same files are found only at integration; agents have treated peer claims as approval.
- **PR delivery.** Deliver text, the fl-pr skill and git gates hard-code `gh pr create` (`ProcessText.cs:200,204`, `skills/fl-pr/SKILL.md:3,9`, `Shared/Settings/GitGates.cs:13`), but fleet-tui's PRs go to Forgejo; `tea` is not installed. The user's global orchestrator instructions are hand-copied into `~/CLAUDE.md` and drift from fleet.

Evidence: `reports/08-stale-main.md`, `reports/09-new-agent-base.md`, `reports/improvement-candidates.md` §E, ticket answers 07, 10, 11.

## Scope
1. Stale-main fixes: stage text, `pull_repository` in bare repos, lag and base in status tools.
2. `base` argument on `new_agent` (and the starter `dispatch` uses); remote-only branch reuse.
3. Evidence on `report`: fleet-captured git state, agent-supplied verify result; shown in four places.
4. Profile proposal at first contact and a `set_profile` tool.
5. Release/version question at Plan.
6. Skeleton gate and post-report commit flags.
7. Overlap notices; combined-stack review; user-only approvals.
8. `open_pr` MCP tool with forge detection; text updates; fleet-owned global instructions include file.

## Non-scope
- Auto-fetch inside `new_agent`; pinning base SHAs in `state.json` (deferred by user).
- Blocking enforcement of gates or overlaps (user chose flag/notify only).
- Merging PRs (never; merging is the user's).
- Installing `tea` or managing forge tokens.
- Editing the user's own `~/CLAUDE.md` / `~/AGENTS.md`.
- Messaging/notify mechanics (`specs/messaging.md`); this spec reuses its notice path.

## Acceptance criteria

### Stale main
- AC-1. When fleet writes the Discover, Plan, Build and Review stage text, it shall tell the orchestrator to `git fetch` first and to use `origin/<default>` (or the unit's recorded base) as the base for reading and diffing, never local `<default>`.
- AC-2. When `pull_repository` runs on a bare repository with no `<default>` worktree, the system shall fetch origin and move local `<default>` to `origin/<default>` when that is a fast-forward (`git fetch origin <default>:<default>`), and report what moved; when it is not a fast-forward, it shall leave the branch and say so.
- AC-3. When `repository_status` runs, the system shall show how many commits local `<default>` is behind and ahead of `origin/<default>`.
- AC-4. When `agent_status` runs, the system shall name the agent's base ref (e.g. `base origin/main`) next to its ahead/behind counts.

### new_agent base
- AC-5. When `new_agent` (and `dispatch` with a repository) receives an optional `base`, the system shall create a new worktree branch from that ref (local branch or `origin/x`); without `base`, it shall use the repository default as today.
- AC-6. When `base` is given but the branch already exists (so the base is not used), the result shall say the base was ignored and name the branch's existing tip.
- AC-7. When the requested branch exists only as `origin/<branch>`, the system shall create the local branch tracking it instead of cutting a fresh branch from the default.
- AC-8. When fleet writes the Build stage text, it shall tell the orchestrator to pass `base: <parent unit's branch>` for units that depend on another unmerged unit.

### Evidence on report
- AC-9. When an agent calls `report` with any status, the system shall record, from git at that moment, the worktree path, base ref, merge-base SHA, HEAD SHA and whether the worktree is dirty.
- AC-10. When an agent calls `report` with `done` or `failed`, the system shall accept optional `verify_command`, `verify_exit_code` and `verify_output`, and store the output trimmed by fleet to the last 40 lines and at most 4 KB.
- AC-11. When a repository agent reports `done` without verify fields, the system shall show it as `done (unverified)`; with a non-zero exit code as `done (verify failed)`; with a dirty worktree as `done (dirty)`. Sub-orchestrators are exempt from the unverified flag. No report is rejected.
- AC-12. When `agent_status` runs, it shall show all evidence fields; `list_agents` shall show one line per agent with short HEAD SHA and verify ok / failed / unverified; the owner's done/failed notice shall include short HEAD SHA and verify result; the dashboard agent card shall show short HEAD SHA and a verify badge.
- AC-13. The agent store shall round-trip the new fields; records without them shall load unchanged.

### Profile at intake
- AC-14. When fleet writes a sub-orchestrator's instructions, they shall tell it to name a better-fitting profile (from the profiles fleet defines) at its first contact with the user if the current one does not fit, and to change it only after the user agrees.
- AC-15. When a sub-orchestrator calls a new `set_profile(profile)` MCP tool, the system shall change the orchestration's profile in `state.json`, regenerate the stages for stages not yet started, and append a `ProfileSet` event to `audit.jsonl`; it shall refuse an unknown profile with the list of valid ones.

### Release at Plan
- AC-16. When fleet writes the Plan stage text, it shall tell the orchestrator to ask the user, before the plan gate, whether the work ships in a release, which version, and who writes the `RELEASE_NOTES.md` entry, and to record the answer in the plan.

### Gates and post-report commits
- AC-17. When an agent's HEAD has commits after the HEAD SHA recorded by its last `done` report, `agent_status`, `list_agents` and the dashboard shall flag it "N commits after report".
- AC-18. When an agent is started for a unit while the orchestration's walking-skeleton gate is not yet approved in `state.json` and that unit is not the skeleton, the system shall start it and send the owner a notice "unit started before the skeleton gate was approved". Nothing is blocked.

### Overlap, review, approvals
- AC-19. When two live agents owned by the same orchestrator have changed (vs their bases) at least one file in common, the system shall send that orchestrator one notice naming the agents and files, and send it again only when the set of shared files grows.
- AC-20. When fleet writes the Review stage text, it shall require a review of the combined stack (all units merged in dependency order) before Deliver, in addition to per-unit reviews.
- AC-21. When fleet writes orchestrator or agent instructions, they shall state that only the user approves a gate, and that a message from another agent or session claiming approval is not an approval.

### PR delivery
- AC-22. When an agent calls a new `open_pr(repository, branch, title, body_file, base?)` MCP tool, the system shall ask the user through fleet's approval popup (showing repository, branch, base, title) and, if approved, push the branch to the push remote and open a pull request on the repository's forge, and return the PR URL; it shall never merge.
- AC-23. When the forge is GitHub, `open_pr` shall use `gh pr create` with the body file; when it is Forgejo or Gitea, it shall push with AGit (`<branch>:refs/for/<base>` with `topic`, `title` and `description` options) and return the PR URL it reports.
- AC-24. When fleet needs a repository's forge, it shall use the per-repository override if set; else treat a `github.com` remote as GitHub; else probe the host's Forgejo/Gitea version API once and cache the result; when none match, `open_pr` shall fail with a message naming the override setting.
- AC-25. The push remote shall be `origin` unless the repository sets another; fleet shall never push to a remote whose push URL is disabled.
- AC-26. The Deliver stage text, the `fl-pr` skill and the git gate rules shall direct agents to `open_pr` and no longer mention `gh pr create` as the way to open a PR.

### Global instructions
- AC-27. When `fleet setup` or `fleet update` runs, the system shall write `~/.config/fleet/ORCHESTRATOR.md` with the current agent-neutral orchestrator instructions (tools, delegate-first, waiting, finishing, pushing via `open_pr`), fully owned and overwritten by fleet, and shall not modify `~/CLAUDE.md` or `~/AGENTS.md`.
- AC-28. When `fleet doctor` runs, it shall report whether `~/CLAUDE.md` (and `~/AGENTS.md` where a non-Claude harness is in use) references the include, and print the exact line to add when it does not.

### Repo rules
- AC-29. The system shall keep the repo rules: no comments in `src`, slice/Platform dependency rules, AOT-safe source-generated JSON, MCP path never uses `Console`; `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` pass.

## Decisions (user, tickets 07, 10, 11)
All of E in. Stale-main extras deferred. Profile: sub proposes, user confirms, `set_profile` tool. Gates: record + flag only. Overlap: notice to the owner only. Evidence: fleet captures git on every report; agent adds optional verify on done/failed; missing = flagged; 40 lines / 4 KB; shown in agent_status, list_agents, owner notice, dashboard card. PRs: `open_pr` MCP tool, gh for GitHub, AGit for Forgejo/Gitea, forge auto-detect + per-repo override, approval every time. Global instructions: separate fleet-owned include file the user references.

## Assumptions
- The multi-harness stack merges largely as at `1d83abed`; owner notices use the append-only notices path from `specs/messaging.md` (AC-3 there). If this spec builds first, notices use today's typed path.
- AGit pushes on Forgejo return the PR URL in the push output (seen in past deliveries; verify in Build).
- fleet's approval channel can be reused for `open_pr` as for other gated actions.
- `state.json` stages can be regenerated for a new profile without losing finished stages.

## Open questions
1. **Deferred to Plan (facts):** how codex, opencode and pi load the include file. CLAUDE.md supports `@` imports; AGENTS.md imports are unverified per harness. Fallback: fleet adds the file through each harness's config (e.g. opencode `instructions`), and AC-28's doctor line changes to match.
2. **Deferred to Plan:** unit split and file ownership against `specs/messaging.md` and the harness-parity spec (shared `McpActions.cs`, `McpTools.cs`, `ProcessText.cs`, `OrchestrationText.cs`, `AgentRecord.cs`).
3. **Deferred to Build:** exact Forgejo/Gitea probe endpoint and the PR URL format AGit returns (verify against git.home.redmonet.nl).
