# Spec: nvim yank in a remote pane reaches the viewer's clipboard

## Problem
In a fleet REMOTE pane, `alt+n`, `Shift+V`, `y` in nvim (Claude in a :terminal) does not reach the viewer's clipboard. Locally it works because nvim finds wl-copy via `$WAYLAND_DISPLAY`. Fleet's bundled nvim config sets no clipboard behaviour, and a headless remote has no provider. Daemon-side OSC 52 relay to the viewer already exists and is tested.

## Decisions (from interview 2026-10-19)
- Every yank (y, yy, visual y, "+y) copies to the viewer clipboard.
- Mechanism: both an OSC 52 copy provider in nvim and copy-direction shims.
- Must work with fleet's bundled nvim config and with the user's own config (`user` setting).
- Copy only: paste from `+` uses nvim's own register; OSC 52 read stays blocked.
- Other copy paths (copy mode, mouse drag, ctrl+shift+c): verify remote, fix only if broken.
- Test matrix: viewer trivium (Wayland/Ghostty); remote headless Linux; remote Linux with `$DISPLAY` set (ssh -X); remote macOS (pbcopy side, best effort).
- Locally it already works; local behaviour must not regress.

## Scope
- Bundled nvim config: OSC 52 clipboard provider (copy only, custom paste returning the register) plus yank-to-`+`.
- Own-config case: fleet injects a minimal startup snippet that sets `vim.g.clipboard` to the same provider only when the user config did not set one.
- Remote copy shims `wl-copy`, `xclip` (-i / copy direction), `pbcopy` that forward text to the viewer over the bridge.
- Tests for each; docs/README note.

## Non-scope
- OSC 52 read / paste from the viewer clipboard as text.
- Version bumps, tags, merging.
- Windows remotes, tmux passthrough.

## Acceptance criteria
- AC-1: When the bundled nvim runs in a remote pane and the user yanks (y, yy, visual y, "+y), the system shall emit the yanked text as an OSC 52 write to the pane.
- AC-2: When fleetd receives that OSC 52 write from a remote pane, the viewer's clipboard shall hold the text (existing path; covered by a regression test using nvim's emitted sequence).
- AC-3: When the user's own nvim config is used and it does not set `vim.g.clipboard`, fleet shall make yanks copy via the same OSC 52 provider.
- AC-4: When the user's own config sets `vim.g.clipboard`, fleet shall not override it.
- AC-5: When a process in a remote pane runs `wl-copy`, `xclip -i` (any selection) or `pbcopy` with text on stdin, the shim shall deliver the text to the viewer's clipboard through the same limits as OSC 52 (text, up to 1 MiB).
- AC-6: When the shimmed copy tool is run with arguments the shim does not handle, it shall fall back to the real tool, or exit 127 with a clear message as today.
- AC-7: When a remote has `$DISPLAY` set and no real xclip, a yank shall still reach the viewer (no silent 127).
- AC-8: When nvim pastes from the `+` register in a remote pane, the system shall use nvim's own register and shall not send an OSC 52 query; OSC 52 read shall remain unanswered.
- AC-9: When nvim runs locally (not a remote pane), yank behaviour shall be unchanged.
- AC-10: When a copy is dropped (oversize, no viewer), the system shall log the reason in fleet.log as today; the shim shall report failure to its caller.
- AC-11: Copy mode, mouse drag and ctrl+shift+c in a remote pane shall still reach the viewer (existing BridgeCopyTests stay green; add a gap test if one is found).
- AC-12: `dotnet build`, `dotnet test Fleet.slnx` and `dotnet format --verify-no-changes` pass.

## Assumptions
- Remote nvim is >= 0.10 (needs vim.ui.clipboard.osc52 or a hand-written provider; we write our own provider so no hard dependency).
- Viewer and remote both run a fleet containing this change; older local fleetd ignores unknown effects (no new effect kinds are added).

## Open questions
- Whether nvim under fleet's emulator sets termfeatures.osc52 (moot with an explicit provider; will be confirmed in the reproduction).
- Exact injection mechanism for the own-config case (`--cmd` flag vs runtimepath file); deferred to Plan.
- Shim transport: reuse the pane clipboard control op vs a new one; deferred to Plan.
- Whether a real remote macOS is available to test; best effort.
