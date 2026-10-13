# Spec: copy/paste over SSH bridge

Stage: Specify. Repo: fleet-tui only.

## Problem
User on Windows (PowerShell / Windows Terminal) runs local fleet attached to a remote project via `ssh -T host fleet bridge`. Copying in a remote pane does not reach the Windows clipboard. A local-pane copy on the same machine works. User wants Ctrl+C / Ctrl+V style copy and paste (Ctrl+Shift+C / Ctrl+Shift+V acceptable).

## Findings so far (static, untested)
- Remote-to-local forwarding chain traced end to end with no break: remote `CopyTarget` -> `HostEffect` -> bridge -> `RemoteLink` -> `FleetDaemon.Forward` (~2353) -> `AttachClient.Apply` -> `SetWindowsClipboard`. See discover.md.
- Local copy works, so `SetWindowsClipboard` is probably fine. Suspects left: remote `CopyTarget` choosing a session other than the bridge link (e.g. iso mode `MayView` false, FleetDaemon.cs:483,624), local relay filter `Showing == "@"+link.Name` (~2371), silent drops (no log when target null).
- No Ctrl+C/V/Shift bindings exist; only prefix `[` `]` `i`. Windows Terminal itself intercepts Ctrl+Shift+C/V before fleet sees them unless unbound in WT settings.

## Scope
- Make remote-pane copy reach the local (Windows) clipboard over the bridge.
- Add direct keys Ctrl+Shift+C (copy) and Ctrl+Shift+V (paste local clipboard into focused pane).
- Add mouse drag-select in fleet that copies on release.
- Surface copy failures to the user (not only the log).

## Non-scope
- Ctrl+C remains interrupt to the pane; Ctrl+V remains passthrough to apps.
- OSC 52 read/query by panes stays refused.
- Plain `ssh -t host fleet` (attach client on remote), tmux passthrough, macOS/Linux viewers: not targeted now (deferred).
- Image paste.

## Acceptance criteria
- AC-1: When a remote pane produces a copy (copy-mode `y` or OSC 52 from an inner app) while the user views that pane through a bridge link, the local viewer's clipboard shall contain that text.
- AC-2: When the remote daemon cannot choose a session to deliver a copy to, the daemon shall log the drop with the reason.
- AC-3: When the viewer fails to set the clipboard, fleet shall show a visible notice, not only a log line.
- AC-4: When the user presses Ctrl+Shift+C in a focused pane, fleet shall copy the current selection (or the copy-mode selection) to the local clipboard.
- AC-5: When the user presses Ctrl+Shift+V, fleet shall read the local clipboard and paste it to the focused pane (remote pane included) as a bracketed paste.
- AC-6: When the user presses Ctrl+C or Ctrl+V, fleet shall send them to the pane unchanged.
- AC-7: When the user drags the mouse over a pane and releases, fleet shall copy the selected text to the local clipboard, for local and bridged remote panes.
- AC-8: While the mouse is captured by fleet, selection shall not break existing divider/float drags.
- AC-9: Tests shall cover the bridge copy path (fake link) and each new key and mouse behaviour, and `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` shall pass.

## Assumptions
- Windows Terminal passes Ctrl+Shift+C/V to fleet only if the user unbinds them in WT settings (copy/paste actions). Otherwise WT handles them itself; WT paste already works as burst paste.
- Remote fleetd is the same build as local.

## Open questions
1. Root cause unconfirmed. Need from user: Windows attach log line (`clipboard: N chars set` or `NOT set`) and remote fleetd log line `<pane> copied N chars to <client>'s clipboard` after a failed remote copy. Recommended: build Unit 1 as a diagnostic + fix (AC-1..3), first.
2. Ctrl+Shift+C/V under Windows Terminal: recommended: document WT unbinding and also keep working paste via WT. Deferred pending user.
3. Mouse select while fleet captures mouse: pane apps that request mouse (nvim) keep getting mouse; fleet selects only when pane has no mouse mode or with Shift held. Recommended; needs confirmation.
