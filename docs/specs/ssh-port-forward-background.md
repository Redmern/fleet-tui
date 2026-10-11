# Spec: background ssh -N -L port forwards (Windows viewer)

## Problem
User views fleet from Windows. Forwarding relies on OpenSSH ControlMaster (`ssh -O forward -L`), which is unsupported on Windows (`ControlPaths.Supported` false, `ForwardHub.For` returns null, "port forwarding needs OpenSSH control sockets"). The Open port flow then only shows an `ssh -N -L p:localhost:p user@host` command to copy by hand. That manual command works. Fleet should run it itself, in the background.

## Scope
- fleet-tui only. When control sockets are unavailable (Windows), fleet forwards by spawning a background `ssh -N -L 127.0.0.1:<local>:localhost:<remote> [-p N] <host>` process on the viewing machine and tracking it.
- Applies to: Open port (viewer route and plain-ssh route when fleet runs on the viewing machine), forward_port / unforward_port / `fleet forward`.

## Non-scope
- No server-side fleet connecting back to the client.
- No change to the ControlMaster path on Linux/macOS.
- Plain ssh login where the viewing machine runs no fleet: dialog with copyable command stays (fleet cannot start a process there).

## Acceptance criteria
- AC-1: When control sockets are unavailable and a forward is requested, the system shall start a background `ssh -N -L` process with `ExitOnForwardFailure=yes`, `ServerAliveInterval`, `BatchMode`/askpass-safe options, and no console window.
- AC-2: When the process is up and the local port accepts connections, the forward row shall be `Forwarded` and, if requested, the browser opens `http://localhost:<local>`.
- AC-3: When the requested local port is busy, the system shall pick a free one (as today) and report it.
- AC-4: When ssh exits early (auth failure, bind failure), the row shall be `Failed` with ssh's stderr as the error.
- AC-5: When unforward is requested for a port, the system shall kill its ssh process and remove the row.
- AC-6: When fleet exits, all background tunnel processes it started shall be terminated.
- AC-7: When the tunnel process dies while the forward is pinned, the system shall restart it with the existing reconnect backoff.
- AC-8: On Linux/macOS the ControlMaster behaviour is unchanged (existing tests pass).
- AC-10: When a user on a viewing machine without control sockets (Windows) runs `fleet attach --ssh <host>` and Open port or forward_port is used on the remote, the viewing machine's fleet shall run the background `ssh -N -L` tunnel itself, open `http://localhost:<local>`, stop it on unforward, and end it when the attach client ends.
- AC-11: When a tunnel's first start fails, the system shall retry it with the reconnect backoff instead of staying failed.
- AC-9: README and docs/DESIGN.md no longer say forwarding is "not on Windows"; they describe the fallback.

## Assumptions
- The viewer's `ssh` is OpenSSH for Windows and key/agent auth works non-interactively (no password prompts for the tunnel).
- Host used is the same ssh host as the link / SSH_CONNECTION-derived address.

## Open questions
- Deferred: attach-client tunnels are not reported to the remote (list_forwards/dashboard do not show them; failures show in the attach log).
- Deferred: password-protected hosts on Windows (askpass for the tunnel). Recommended: fail with clear error; use keys.
