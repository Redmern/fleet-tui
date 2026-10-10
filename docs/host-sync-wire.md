# Host sync: wire format (Phase 0 note)

Status: decided, spiked in `tests/Fleet.Tests/Platform/Sync/SyncWireSpikeTests.cs`. Nothing in fleetd speaks it yet;
Phase 2 adds the receiving side.

## What the transport already is

`ssh -T <host> fleet bridge` (`SyncSpawn.Bridge`, mirroring `EmbeddedWiring.RemoteSsh`) starts `fleet bridge` on the
remote. `BridgeAsync` connects to that machine's fleetd and copies bytes both ways, untouched. So whatever crosses the
bridge is simply fleetd's own `Wire` protocol: a 5-byte header (4-byte little-endian length, 1-byte `MessageType`) and a
payload of up to `Wire.MaxMessage` (16 MiB). The ssh channel is binary-clean (`-T`, no pty), so raw bytes are safe.

## Options weighed

1. **A `sync.*` request family with the bytes in JSON** (base64 in `ControlRequest.Args`). Simple, but costs a third more
   bytes, puts whole files through the JSON serializer, and every chunk is a request/response round trip.
2. **A framed binary channel inside fleetd** (chosen). Control stays JSON; bytes travel in their own message type.

## The chosen format

| Step | Message | Payload |
|---|---|---|
| open | `Request`, op `sync-open` | JSON `ControlRequest`, `Args = [id, kind, name, size, sha256]` |
| data | new `MessageType.SyncData = 40` | 16-byte stream id (`Guid`) + up to 64 KiB of raw bytes |
| close | `Request`, op `sync-close` | JSON, `Args = [id]`; fleetd answers with a `Response` after verifying size and sha256 |

- `kind` is `bundle` (a `git bundle`, Phase 2), `tar` (Phase 3) or `secrets` (Phase 4). The receiver decides where it
  lands; the sender never names a destination path for `bundle`.
- The stream id lets one connection carry several streams (a bundle and a tar) and keeps a stray frame from being
  appended to the wrong file.
- 64 KiB data frames keep a single message far below `Wire.MaxMessage` and let control traffic interleave.
- The receiver writes to a temporary file, verifies size and sha256 on `sync-close`, and only then applies it. A broken
  connection leaves nothing applied; v1 retries the whole stream (no mid-stream resume).

## Talking to an older fleet

An older fleetd silently ignores an unknown `MessageType`, but it answers an unknown `Request` op with
`unknown operation '...'`. That is why open and close are requests: `sync-open` against an old peer fails at once with
a clear error before a single data frame is sent. Phase 6 turns that error into "update fleet on <host>".

## ISO

None of this runs unless `SshSyncEgress` lets it: its first step checks machine ISO, then project ISO, then a Forbid rule
on `sync_to_remote`, and refuses with `SyncRefusedException` and an audit line before `SyncSpawn` is ever reached. Only
`Platform/Sync` may reference `SyncSpawn` (`SliceBoundaryTests`). ISO only covers transfers fleet itself starts; a user
running git or ssh by hand is out of its reach.
