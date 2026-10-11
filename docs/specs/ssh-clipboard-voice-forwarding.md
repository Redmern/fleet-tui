# Spec: clipboard, image paste and voice over fleet remotes

Repository: fleet-tui only. Profile: feature. Stage: Specify (awaiting user approval).

## Problem
Working on a remote machine through fleet (`ssh -T host fleet bridge`), the user cannot paste images, clipboard behaviour is unreliable, and Claude Code `/voice` does not work. Causes (see discover.md):
1. Claude Code reads clipboard images with xclip/wl-paste/osascript/PowerShell on the machine it runs on; a remote host has no clipboard and fleet never ships the viewer's clipboard.
2. Pane clipboard reads are refused by design; only a Windows-only text reader exists; Linux/mac have none.
3. Fleet never sends `?2004h` to the host terminal, so pastes on Linux/mac are not bracketed.
4. Text copy relies on OSC 52 only, success is always reported, and it is silently dropped where the terminal or a multiplexer blocks it.
5. Fleet forwards no audio; `/voice` records from the headless remote.

## Decisions (from the interview)
- Connection: fleet bridge only. Plain `ssh host; claude` is out of scope.
- Image paste: viewer ships its clipboard image over the wire on demand; the remote fleet serves it through clipboard shims, so Ctrl+V in Claude Code just works.
- Remote OS: shims for Linux, macOS and Windows remotes (viewer may be any of the three).
- Trigger: on demand, only when something in the pane runs the shim. Fleet does not intercept Ctrl+V.
- Consent: images only, only to the pane the user is focused on. No OSC 52 read, no text reads by panes.
- Text: send `?2004h`, add Linux/mac clipboard readers and a native copy fallback.
- Voice: spike first (Linux/mac viewer Pulse-socket reverse forward + `PULSE_SERVER` in the pane), decide on more after the spike.

## Scope
In: wire messages for clipboard image request/response; viewer clipboard image readers (Windows, Linux wl-paste/xclip, macOS); remote shims and pane env; `?2004h` in EnterHost; text clipboard readers and native copy fallback; voice spike; docs/DESIGN.md and README updates.
Out: plain ssh without fleet; OSC 52 read or kitty OSC 5522 support; Windows-viewer voice (deferred until the spike result); changing Claude Code.

## Acceptance criteria
- AC-1. When the host terminal is entered on Linux or macOS, the attach client shall enable bracketed paste (`?2004h`) and disable it on restore.
- AC-2. When a paste arrives with bracket markers and the pane has enabled bracketed paste, the system shall deliver it to the pane as a bracketed paste.
- AC-3. When a pane process runs the clipboard shim and the pane is focused in a viewer, the system shall request the viewer's clipboard image over the wire and return the PNG bytes to the shim.
- AC-4. When the viewer clipboard holds no image, the shim shall exit with the same status and empty output its native tool gives for "no image", so Claude Code reports no image.
- AC-5. When the shim is called by a pane that is not the viewer's focused pane, the system shall refuse and the shim shall report no image.
- AC-6. When a pane sends OSC 52 clipboard read, the system shall continue to refuse it.
- AC-7. The viewer shall read clipboard images on Windows, Linux (wl-paste, then xclip) and macOS, and return nothing if no reader or image is present.
- AC-8. Image payloads shall be limited to a maximum size (the 16 MiB wire message limit, which after base64 is about 12 MiB of image); larger images shall be refused with a clear error.
- AC-9. When a remote pane starts on a Linux, macOS or Windows host, fleet shall provide a shim set (xclip/wl-paste; osascript/pngpaste equivalent; PowerShell equivalent: see open questions) in the pane environment.
- AC-10. When a copy occurs in a pane on Linux or macOS and the outer terminal does not accept OSC 52, the viewer shall fall back to wl-copy, xclip or pbcopy, and shall report failure only if all fail.
- AC-11. When a local (non-remote) pane runs, the system shall not change its clipboard behaviour.
- AC-12. When the viewer is Linux or macOS and the remote has a PulseAudio/PipeWire socket route, fleet shall reverse-forward the viewer's pulse socket through the ControlMaster and set `PULSE_SERVER` in remote panes (voice spike), behind an opt-in setting.
- AC-13. The voice spike shall record, in docs, whether Claude Code `/voice` captures audio on a headless remote via the forward, with the exact steps and versions tested.
- AC-14. Every new wire type shall be registered in `WireJsonContext`; `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` shall pass with no comments in `src`.
- AC-15. docs/DESIGN.md ("Window title and clipboard") and README shall describe the new clipboard-image, paste and voice behaviour and the consent rule.

## Assumptions
- Claude Code finds the shims first on PATH (it invokes `xclip`, `wl-paste`, etc. by name). Verify in build.
- The ControlMaster `-O forward -R` of a unix socket works on Linux/mac viewers; Windows viewers have no ControlMaster.
- Claude Code `/voice` may not honour `PULSE_SERVER`; the spike result decides what ships.

## Open questions (resolved at Specify gate, user: "go with your recommendations")
1. Windows remote: no PATH shim; use the temp-file-plus-path fallback (viewer image saved on the remote, path typed into the pane). Resolved.
2. Shim delivery: multi-call `fleet` binary (`fleet shim <tool>`), linked/dispatched from a fleet-managed bin dir on the pane PATH. Resolved.
3. Remotes not running Claude Code from a fleet pane: out of scope. Resolved.
