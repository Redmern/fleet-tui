# Spec: Open port on the viewer route reports its outcome and stops failing silently

Repository: fleet-tui. Stage: Specify (awaiting approval).

## Problem
With a laptop fleet linked under Remote machines, Open port on a remote project sends a `forward-port` effect to the laptop. Whatever the laptop does next (forward, fail, drop the effect, fail to open the browser) is invisible: errors go only to the laptop's fleet.log, success shows no dialog, and the menu closes before anything settles. The user sees "nothing happens" and falls back to a manual `ssh -N -L`, which prompts for a password. The viewer route never prompts by design (it reuses the link's authenticated master).

## Scope
- Viewer route: report forward outcome (success, pending, failure with reason) back to the Open port dialog.
- Make the known silent-failure paths loud or self-healing (unknown effect dropped, effect task exceptions, failed-forward blocks retries, browser open failure).
- Dialog text and docs state which route ran.
- Root-cause fix for whatever the laptop diagnostics show (see Open questions).

## Non-scope
- `fleet attach --ssh` client running the tunnel itself or prompting for a password (decided: skipped). The plain-ssh copy-command route stays as is.
- New password mechanisms for forwards.
- fleet-frontend (no forward code).

## Acceptance criteria
- AC-1: When Open port uses the viewer route and the viewer forwards the port, the dialog shall say so and name the local URL (`http://localhost:<port>`), instead of closing silently.
- AC-2: When the viewer's forward fails, the system shall return the failure reason (ssh stderr, trimmed) to the Open port dialog on the remote.
- AC-3: When the remote port is not yet listening, the dialog shall say the forward is pending and the viewer forwards once it listens, and the viewer shall open the browser when the forward later settles.
- AC-4: When the viewer's fleetd receives an effect kind it does not handle, it shall log that it ignored it, and the remote shall be told the viewer cannot forward so Open port falls back to the ssh command route.
- AC-5: When the viewer's effect task throws any exception, the system shall log it and report failure per AC-2.
- AC-6: When a forward for a port previously failed and the user runs Open port again, the system shall retry it (a failed pin shall not block a re-pin).
- AC-7: When the viewer cannot open the browser, the dialog shall show the URL so the user can open it manually.
- AC-8: When the viewer route reports no answer within the forward timeout (20 s), the dialog shall say the viewer did not respond and point to the viewer's fleet.log.
- AC-9: When the viewer fleetd build differs from the remote's in a way that lacks `forward-port` handling, the Open port dialog shall tell the user to restart the viewer's fleetd.
- AC-10: Tests shall cover AC-1 to AC-9 with hand-written fakes; `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes` pass.

## Assumptions
- Design stays within the existing architecture rules (no comments in src, slices isolated, composition in `Cli/Composition`).
- Result back-channel uses the existing viewer-forward op/response (extend, not new transport).
- Version bump + RELEASE_NOTES entry needed.

## Open questions
1. Laptop diagnostics (fleetd build, laptop fleet.log, control socket, `ss`) not yet received. The real cause may be one of hypotheses 1-5 in discover.md. DEFERRED to Plan: if diagnostics arrive before Plan approval, add a targeted root-cause AC; otherwise the loud-failure ACs make the cause visible on first retry.
2. Should the Open port dialog block up to 20 s waiting for the result (AC-8), or show "forwarding..." and update? Recommendation: a Wait dialog (existing `FleetDialog.Wait<T>`).
