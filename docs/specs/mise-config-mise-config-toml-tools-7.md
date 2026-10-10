# Spec: "Open port" action

Repo: fleet-tui (C#/.NET 10). Branch off `feat/ctrl-s-fleet-actions-toggles` (decided); PR targets that branch.

## Problem
A user viewing fleet from another machine cannot reach a web app listening on the machine fleet runs on without manual ssh/forward setup. Existing forward tooling pulls only from an explicit `remote`.

## Scope
1. New "Open port" action in the fleet menu (Session section) and ctrl+s `s o`. One input: port number.
2. Viewer cases, decided by fleetd/client:
   - **Fleet laptop** (linked via Remote machines or `fleet attach --ssh`): new `viewer-forward` op, modeled on `viewer-open`; laptop ForwardHub pins + forwards (`-O forward -L`) and opens `http://localhost:<port>` in its browser.
   - **Plain ssh**: dialog with copyable `ssh -N -L <p>:localhost:<p> <user>@<host>` from SSH_CONNECTION captured in the client/attach process.
   - **Local**: open `http://localhost:<port>`.
3. New `viewer-unforward` op (symmetric; unpins and cancels).
4. `fleet attach --ssh` shares RemoteLink's ssh args/ControlMaster options (one builder, no duplication).
5. forward_port / unforward_port: `remote` optional in MCP and Head tool sets; omitted = this host -> my viewer. Remove redundant code (required-Remote checks, `HeadRemotes.IsLocal` rejection, empty-host fallback in ForwardPortsHandler). Routes through IPortForwards.
6. ISO: `viewer-forward` and `viewer-unforward` added to IsoFilter whitelist (allowed everywhere; user decision, noted weaker isolation).
7. README (~319-353) and docs/DESIGN.md (~5833-5900) updated.

## Non-scope
Removing `viewer-open`/open_url viewer branch; CLI `fleet forward <host>` changes; Windows (no control sockets); fleet-frontend; merging.

## Acceptance criteria
- AC-1: When the user picks Open port in the fleet menu or presses ctrl+s `s o`, the system shall ask for one port number and act on enter.
- AC-2: When the input is not an integer in 1-65535, the system shall show an error and not forward.
- AC-3: When the viewer is a fleet laptop, the host daemon shall send a `viewer-forward` op to the viewer session, whose ForwardHub shall pin and forward `-L` that port and open `localhost:<port>` in its browser.
- AC-4: When no fleet viewer session exists but the client was reached by ssh, the system shall show a dialog with `ssh -N -L <p>:localhost:<p> <user>@<host>` from SSH_CONNECTION captured in the client/attach process (not fleetd), copyable.
- AC-5: When no ssh is between host and viewer, the system shall open `http://localhost:<port>`.
- AC-6: When nothing listens on 127.0.0.1:<port> on the fleet host, the dialog shall say so and still forward / show the command / open.
- AC-7: When `fleet attach --ssh` connects, it shall use the same ssh arguments and ControlMaster options as RemoteLink from one shared builder.
- AC-8: When forward_port or unforward_port is called without `remote`, the system shall route to viewer-forward / viewer-unforward via IPortForwards; with `remote` behavior is unchanged.
- AC-9: When viewer-unforward is received, the viewer ForwardHub shall unpin and cancel the forward.
- AC-10: When ISO is on, viewer-forward/viewer-unforward shall be served (whitelisted in IsoFilter); a test pins this.
- AC-11: Code made redundant by AC-8 shall be removed; architecture tests still pass.
- AC-12: README and DESIGN.md document the action, ops, optional remote, and ISO decision.
- AC-13: Fake-based tests cover the op (host and viewer sides), the action (all 3 viewer cases + no-listener + bad input), and the keybinding/menu tests the branch requires.
- AC-14: `dotnet build` (warnings as errors), `dotnet test`, `dotnet format --verify-no-changes` pass; commits lower-case conventional.
- AC-15: The final report includes manual steps for laptop->trivium (fleet on laptop, and plain ssh).

## Assumptions
- Viewer detection: viewer session in `_viewerForwards`/linked viewer => fleet laptop; else SSH origin present => plain ssh; else local. Exact mechanism chosen in design.
- Listener probe runs on the host against 127.0.0.1:<port>.
- Menu icon OpenBrowser; chord `s o` free on the branch (verify).
- Delegate to one agent (plumbing and UI share ops/IPortForwards; split risks file overlap).

## Open questions
None blocking. Deferred: the IsoFilter weakening (bridged ISO clients can trigger viewer-side forwards) is accepted per user.
