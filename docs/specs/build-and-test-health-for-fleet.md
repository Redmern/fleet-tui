> Approved by the user on 2026-11-12. Copied verbatim from `/home/red/repos/fleet-tui/.fleet/orchestrations/fleet-multiharness-interop-improvements/specs/build-health.md`; the Specify interview was skipped per TASK.md.

# Spec: build and test health

Repository: fleet-tui. Map: `map.md`, tickets `16-build-health-scope`, `17-flaky-tests`, `18-windows-ci` (all closed). Stage: Specify. Status: **approved by user 2026-11-12**.
Base: `origin/main` = `a00fabfe`. Test paths under `tests/Fleet.Tests`, source under `src/Fleet`; references from research on `1d83abed` are re-checked at Plan.

## Problem
- **Slow suite.** ~5300 tests take ~59 s; ~56 s of that is three serial blocks: the `config-home` collection (294 tests, 27 s, critical path of the parallel phase) and the `modal-depth` (9 s) and `wall-clock` (20 s) collections with `DisableParallelization = true` (`WallClockCollection.cs:8`, `ModalDepthCollection.cs:6`). The slowest tests wait out fixed real timeouts: `RemoteLinkTests.cs:504` `Task.Delay(3 s)`, `JsonAgentStore.cs:149` 5 s lock deadline, `RemoteForwardTests.cs:49` `ViewerAnswerWithin = 3 s`, an `HttpReleaseClient` 6 s delay; the mixed-team e2e spends ~12 s.
- **Flaky timing tests.** `MenuOpenLatencyTests.cs:200` (`held <= 100 ms`) and `RemoteForwardTests.cs:594` (`Elapsed < 3 s`) / `RemoteLinkRefresh` have failed on GitHub's Windows runners. One OOM (exit 137) on the small Forgejo runners.
- **No Windows run before merge.** Only GitHub's `.github/workflows/ci.yml` (and `release.yml`) run Windows, and only on `push: [main]`, `pull_request` (GitHub PRs, unused) and `workflow_dispatch`. Forgejo PR branches get no Windows run; the 46-commit multi-harness stack never ran on Windows before merging. A failing "Installer output" step skips `dotnet test` on that leg.
- **Docs drift.** Repo `CLAUDE.md:11` says "~925 xunit tests, ~40 s"; `CLAUDE.md:71` says the mirrored branch "still runs the full `.github/workflows/ci.yml`", which is false.
- **Missing test seams.** `Cli/Composition/McpActions.cs` builds its collaborators itself (harness configs, slug namer, clock, `ClaudeWiring.TrustFolder`), so tests cannot call `dispatch`/`new_agent` through the real wiring without writing `~/.claude.json`, `~/.codex`, `~/.pi`. Composition wiring (`McpActions`, `DashboardWiring`) has no tests (multi-harness retro).
- **Unsafe smoke tests.** Running fleet binaries outside a sandbox has stopped the real daemon and touched real config in past tasks; the `env -i` recipe lives only in prose.
- **Thin UI tests.** Board view, tabbed pickers and menu actions lack headless harnesses; key tables in README/DESIGN can drift from the keymap.
- **No LICENSE file.**

Evidence: `reports/17-flaky-tests.md`, `reports/18-windows-ci.md`, `reports/improvement-candidates.md` §B, §F, ticket 16 facts.

## Scope
1. Faster suite (≤ 35 s locally) and flaky-test handling.
2. GitHub CI on every mirrored branch push; installer step no longer skips tests.
3. Repo `CLAUDE.md` corrections.
4. `McpActions` test seams.
5. Sandbox smoke script.
6. Headless UI test harnesses.
7. MIT LICENSE.

## Non-scope
- A Windows Forgejo runner (not worth it per research).
- Showing GitHub CI results on Forgejo PRs (beyond a link in PR text).
- Live harness proofs (`specs/harness-parity.md` AC-24).
- Rewriting tests beyond the named hotspots to chase a lower target.
- Release process changes (version bump/tag flow stays).

## Acceptance criteria

### Suite speed
- AC-1. When the suite runs, tests that today wait out a fixed production timeout (remote viewer answer, agent-store lock deadline, release-client retry delay, remote-link delay) shall inject a short timeout or a fake clock, and no single test outside the `Live` and `Perf` categories shall take longer than 2 s.
- AC-2. Tests in the `config-home` collection shall each get an isolated config home (no shared process-wide `FLEET_CONFIG_HOME`) so they run in parallel; the `wall-clock` and `modal-depth` collections shall run in parallel with other tests unless a test truly needs global state, and each remaining serial collection shall say why in its name or a test comment.
- AC-3. When a plain `dotnet test Fleet.slnx --nologo -v minimal` runs on the developer machine, it shall finish in ≤ 35 s wall time; the Forgejo runner time shall be recorded in the PR text (not a target).
- AC-4. The mixed-team end-to-end test shall use a fake clock or injected delays instead of real waits, and keep all its current assertions.

### Flaky tests
- AC-5. When a test asserts elapsed time, its bound shall be relative to an injected clock or measured baseline where possible; a true latency assertion shall retry once automatically before failing.
- AC-6. A test still failing intermittently after AC-5 shall move to a `Perf` category; CI shall run and report the `Perf` category but not fail the build on it.
- AC-7. The suite's peak memory shall not grow: CI on the Forgejo runner shall pass with the existing `DOTNET_gcServer=0` setting.

### Windows CI
- AC-8. When any branch is pushed to the GitHub mirror, `.github/workflows/ci.yml` shall run its Windows and Linux legs, with `concurrency` cancelling an in-progress run for the same branch.
- AC-9. When the "Installer output" step fails, the `dotnet test` step shall still run on that leg and the job shall fail with both results visible.
- AC-10. The fl-pr skill's PR text shall include a line linking the GitHub CI run for the branch.

### Docs
- AC-11. Repo `CLAUDE.md` shall state the current test count and wall time (from the AC-3 run), and describe CI accurately: Forgejo runs the Linux light half on PRs; GitHub runs Windows + Linux + AOT on every mirrored branch push (after AC-8).

### Test seams
- AC-12. `McpActions` shall receive its harness config writers, slug namer, clock and folder-trust writer through constructor/composition parameters, with the composition root passing today's implementations; behaviour shall be unchanged.
- AC-13. A test shall call `dispatch` and `new_agent` through the real `McpActions` wiring with fakes for those seams and a temp HOME, and assert no file outside the temp dirs is written.

### Sandbox smoke
- AC-14. A script `scripts/sandbox-smoke.sh` (and `.ps1`) shall run a given fleet binary under `env -i` with a temporary HOME, XDG dirs, `XDG_RUNTIME_DIR` and `FLEET_ENDPOINT`, run `fleet --help`, `fleet doctor` and a daemon start/stop inside that sandbox, and refuse to run if the sandbox endpoint equals the real `FLEET_ENDPOINT`.
- AC-15. Repo `CLAUDE.md` shall point to the script instead of the prose recipe.

### UI harnesses
- AC-16. The test project shall have headless harnesses that open the board view, the tabbed pickers and the menu, drive them with keys, and assert the resulting state, with at least one test each.
- AC-17. A test shall check that every key listed in README's and DESIGN's key tables matches the dashboard keymap, and fail naming the mismatched key.
- AC-18. A test shall check that every `FleetAction` is wired in each place the codebase requires (menu, keymap, handler, docs), and fail naming the missing place.

### License
- AC-19. The repository shall have a `LICENSE` file with the MIT license text and the line "Copyright (c) 2026 fleet contributors", and README shall name the license.

### Repo rules
- AC-20. The repo rules hold: no comments in `src`, slice/Platform dependency rules, AOT-safe JSON, MCP path never uses `Console`; `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` pass on Linux and Windows CI.

## Decisions (user, ticket 16)
All of B2–B5 and F1–F3 in (B1 already fixed on main). Flaky: fix + one retry, then `Perf` category. CI: all mirrored branch pushes with cancel-in-progress. Suite target ≤ 35 s locally. License: MIT, "fleet contributors".

## Assumptions
- GitHub Actions free minutes cover ~7 min per branch push (public mirror).
- xunit (v2/v3 as used by the repo) supports per-collection parallelism and a custom retry for the latency tests without a new package, or a small in-repo attribute suffices.
- The 2 s per-test limit (AC-1) is measured on the developer machine.

## Open questions
1. **Deferred to Plan:** which `FleetAction` "places" AC-18 checks (from the existing 5-place checklist in past learnings); exact retry mechanism for AC-5; whether `config-home` isolation needs a production change (config home passed in rather than read from env).
2. **Deferred to Plan:** unit split and file ownership against the other three specs (F1 `McpActions.cs` seams should land before the messaging/orchestration/harness units that edit `McpActions.cs`, to give them tests).
