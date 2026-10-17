# Spec — Linux menu navigation lag

## Problem
On Linux (local Ghostty, embedded `fleetd` mux) the fleet menu opens fine but navigating inside it (keypress → redraw) is very slow. On Windows it is fast. Root cause is unknown: static reading shows only ~50-60 ms of fixed delays (Terminal.Gui Unix input poll ~20 ms, iteration cap ~25 ms, daemon frame delay 4 ms), which does not explain "very slow". See discover.md.

## Scope
- Measure key→frame latency on Linux and identify the real cause(s).
- Fix proven causes so navigation is noticeably faster on Linux.
- Check whether other Terminal.Gui screens (dashboard, pickers) share the lag.
- Cross-platform changes allowed if Windows tests stay green.

## Non-scope
- Menu open/cold-start latency (user reports it is fine).
- SSH / remote / tmux-driver performance.
- Features or UI changes unrelated to latency.
- Hard latency numbers (target is "noticeably faster").

## Acceptance criteria
- AC-1: When a maintainer runs the measurement harness on Linux, the system shall report per-keypress key→frame latency (p50/p95) for the menu, and for the dashboard and pickers.
- AC-2: When the harness runs, the system shall report which hops (Terminal.Gui driver, daemon render loop, daemon request/reveal gating, theme watcher) contribute the delay, and the findings shall be recorded in `docs/` or the PR.
- AC-3: When a key is pressed in the menu on Linux, the system shall redraw measurably faster than the baseline recorded under AC-1 (improvement stated in the PR with before/after numbers).
- AC-4: When navigating with the keyboard in the menu, the system shall not issue a synchronous daemon request or full-redraw on the UI thread per keypress (verified by test or log assertion).
- AC-5: When the change is built, the existing test suite (`dotnet test Fleet.Slnx`) shall pass, including `MenuOpenLatencyTests`, `WarmMenuSpeedTests` and `MenuTiming*Tests`, with no Windows regression.
- AC-6: When the menu opens, open latency shall not regress versus baseline.
- AC-7: Each fix shall be backed by measurement evidence that its hypothesis was the cause; unproven hypotheses shall not be "fixed" speculatively.

## Assumptions
- User's Linux box is Omarchy with Ghostty, running the embedded mux.
- Terminal.Gui driver differs per OS and is a prime suspect (unverified).
- Menu is a separate `fleet menu` process in a PTY owned by `fleetd`.

## Open questions (deferred to measurement)
- Which hop causes the lag? Resolved by Unit 1.
- Do other Terminal.Gui screens lag? Resolved by Unit 1.
- Whether fix needs a Terminal.Gui driver change (`ForceDriver`) or daemon change — decided at Plan after Unit 1; Plan gate will revisit.
