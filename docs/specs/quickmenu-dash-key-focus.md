# Spec: dashboard key `d` and focus on the fleet pane

## Problem
The "Go to dashboard" action (`FleetAction.FocusMain`, id `main-pane`) is bound to `m` in the ctrl+s which-key
group `a` (`a m`) and in the fleet menu (`m`). User wants `d` (for dash). After running it, focus ends on the claude
pane instead of the fleet (dashboard) pane.

## Scope
- fleet-tui only.
- Default key change in both surfaces; focus fix on both paths; tests, README, HelpCommand text, RELEASE_NOTES.

## Non-scope
- Migrating users' custom `embedded-keys.json` / keybind overrides (no migration; note in RELEASE_NOTES).
- Keeping `m` as an alias.
- Other keys or actions.

## Acceptance criteria
- AC-1: When the user presses ctrl+s then `a` then `d`, the system shall run the Go to dashboard action (`menu main-pane`).
- AC-2: The default ctrl+s `a m` binding shall no longer exist; `a` group keys stay unique.
- AC-3: When the user presses `d` in the fleet menu, the system shall run Go to dashboard; the default key for `FocusMain` is `d`, not `m`, and no other action in the same menu level uses `d`.
- AC-4: When Go to dashboard runs from the which-key popup, the system shall leave keyboard focus on the project's dashboard pane, not the claude pane.
- AC-5: When Go to dashboard runs from the fleet menu, the system shall leave keyboard focus on the project's dashboard pane, not the claude pane.
- AC-6: README, `fleet help` text and RELEASE_NOTES shall show `d` and mention that custom key overrides are unchanged.
- AC-7: `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` pass on fleet-tui.

## Assumptions
- "dash" means the existing Go to dashboard action.
- Root cause of the focus bug is unknown; the build agent must reproduce it first (suspects in discover.md) and fix it, with a failing test first where unit-testable.

## Open questions
None.
