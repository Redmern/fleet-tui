# Voice over ssh (spike)

Claude Code `/voice` records from the microphone of the machine it runs on. In a fleet remote pane that is the
remote host, which usually has no microphone. This spike lets a remote pane record from the **viewer's**
microphone by forwarding the viewer's PulseAudio/PipeWire socket over ssh.

Status: **plumbing built and unit-tested; not yet proven end to end with `/voice` on a headless host.** See
[What was tested](#what-was-tested).

## How it works

1. On the viewer (Linux or macOS), set `FLEET_VOICE_FORWARD=1` in the environment fleetd starts in. Without it
   nothing below happens.
2. fleetd finds the viewer's pulse socket: `PULSE_SERVER=unix:<path>` if set, else
   `$XDG_RUNTIME_DIR/pulse/native`. If there is none it logs why and forwards nothing.
3. Each time the link to a remote comes up, fleetd runs over the existing ssh ControlMaster:
   - if it forwarded to this host before, `ssh -S <cm> -O cancel -R …` for that forward first, so a link that
     reconnects while the master lives does not end up with a removed socket under a live forward (a failed
     cancel, e.g. because the master died, is ignored);
   - `sh -c '…'` on the remote, which creates `$XDG_RUNTIME_DIR/fleet-pulse` (or `/tmp/fleet-pulse-<user>`
     when there is no runtime dir or the path would be too long), sets it to mode 700, removes an old socket
     of the same name and prints the folder;
   - `ssh -S <cm> -O forward -R <folder>/pulse-<id>.sock:<viewer pulse socket> <host>`.

   fleetd takes the last non-empty line the script printed as the folder and refuses anything that is not an
   absolute path free of `:` and whitespace (a login banner or `.bashrc` output would otherwise end up in the
   `-R` spec). A viewer socket path with `:` is refused too. Every ssh call is bounded (20 s), and a link-up
   step that throws or hangs is logged and retried; it never stops port scanning for the host.

   `<id>` is a hash of the viewer machine and user, so each viewer has its own socket and a reconnect reuses
   the same name. Removing the old socket first stands in for `StreamLocalBindUnlink`, which is an sshd
   setting fleet cannot change.
4. On the remote, fleetd sets `PULSE_SERVER=unix:<socket>` in the env of every new pane it spawns for a
   remote viewer, choosing the newest `pulse-*.sock` in that folder that still answers a connect. A dead
   socket is never handed out, so panes on a remote that nobody forwards to keep their own audio.

Code: `Platform/Forwards/ReverseForward.cs` (paths, prepare script, pane env),
`Platform/Forwards/ReverseForwardPulse.cs` (viewer side), `SshControl.ForwardSocket`/`CancelSocket`,
`ForwardOptions.LinkUp` (runs once per link-up), wiring in `Cli/Composition/EmbeddedWiring.cs`.

### Limits

- Panes spawned before the forward exists do not get `PULSE_SERVER`; restart Claude in them.
- When the link drops the forward dies with the ControlMaster; panes keep a `PULSE_SERVER` pointing at a dead
  socket until the link is back (same name, so it works again after reconnect).
- Only the fleetd remote link (`fleet` connecting to a remote) forwards. `fleet attach --ssh` does not.
- Windows viewers: no ControlMaster, so no forward. Deferred.
- macOS viewers need a running PulseAudio (e.g. Homebrew `pulseaudio`) and `PULSE_SERVER=unix:<socket>`
  set for fleetd; macOS has no `$XDG_RUNTIME_DIR` default. Untested.
- Anyone who can open the remote socket can listen to the viewer's microphone. The folder is mode 700, so
  that is the remote user and root.

## What Claude Code does (2.1.289, Linux)

These findings come from reading the strings of the shipped 2.1.289 binary
(`~/.local/share/mise/installs/claude/2.1.289/claude`, the bundled `audio-capture` module), not from documentation.
They are internal behaviour and **may change in any Claude Code release**; re-check them against the version you
run:

- `/voice` is refused outright only when `CLAUDE_CODE_REMOTE` is set. There is no ssh detection in the
  availability check.
- Recorder order, first that works:
  1. Native module (`audio-capture.node`, Rust `cpal` over ALSA/libasound), **only if `/proc/asound/cards`
     lists a card**. A headless host has none, so this is skipped there.
  2. `arecord -f S16_LE -r 16000 -c 1 -t raw` on ALSA's **default** device, if a 150 ms probe works.
  3. SoX `rec` (16 kHz mono raw).
- So on a headless remote `/voice` uses `arecord` or `rec`, and those reach the forward only through
  PulseAudio:
  - `rec`: install `sox` and `libsox-fmt-pulse` (Debian/Ubuntu). SoX's pulse driver honours `PULSE_SERVER`.
  - `arecord`: ALSA's default must be the pulse plugin (`libasound2-plugins`, then
    `pcm.!default { type pulse }` and `ctl.!default { type pulse }` in `~/.asoundrc`). With **pipewire-alsa**
    the default is `type pipewire`, which ignores `PULSE_SERVER` (verified below).
  - If `arecord` is installed but its probe fails, Claude falls through to `rec`, so a host with only SoX +
    pulse works.

## What was tested

On the viewer: Arch Linux (omarchy), kernel 7.2.5, PulseAudio-on-PipeWire 1.6.8, OpenSSH 10.5p1, alsa-plugins
1.2.12, pipewire-alsa 1.6.8, Claude Code 2.1.289.

- Unit tests (fake ssh): `-O forward -R` arguments, opt-in, viewer socket lookup, per-viewer socket name,
  remote folder choice, link-up hook per (re)connect, pane env picks only an answering socket.
- The remote prepare script runs under real `sh` and picks the same folder fleetd looks in, mode 700, old
  socket removed.
- `PULSE_SERVER=unix:$XDG_RUNTIME_DIR/pulse/native arecord -D pulse …` with `XDG_RUNTIME_DIR` unset recorded
  ~0.9 s of audio; the same with a non-existent socket failed with "Connection refused". So the ALSA pulse
  plugin honours `PULSE_SERVER`.
- `arecord` with Claude's exact arguments on the default device recorded with a bogus `PULSE_SERVER`: on a
  pipewire-alsa host the default device ignores `PULSE_SERVER`.

**Not tested** (no headless ssh target was available, and no ssh key login to localhost). These end-to-end steps
are still to run:

1. A real `-O forward -R` of the viewer's pulse socket through a ControlMaster to a remote sshd, and `pactl info`
   in a remote pane reaching the viewer's server.
2. Claude Code `/voice` capturing and transcribing speech on a headless remote through the forward.
3. SoX `rec` with `libsox-fmt-pulse` on that remote (SoX is not installed on the test machine), and `arecord`
   with ALSA's default set to the pulse plugin.
4. A macOS viewer running PulseAudio with `PULSE_SERVER=unix:<socket>`.
5. A reconnect while the ControlMaster survives, and one after it died, on a real sshd.

## To finish the spike

On a headless Linux remote with fleet and Claude Code (logged in to claude.ai) installed:

```sh
# remote, once
sudo apt install sox libsox-fmt-pulse        # or: alsa pulse plugin + ~/.asoundrc as above

# viewer
FLEET_VOICE_FORWARD=1 fleet                  # start fleetd with the opt-in; connect to the remote
# fleet log should show: forwarded the pulse socket … to …/fleet-pulse/pulse-<id>.sock

# in a new remote pane
echo $PULSE_SERVER                           # unix:…/fleet-pulse/pulse-<id>.sock
pactl info                                   # the viewer's server
rec -q -t raw -r 16000 -e signed -b 16 -c 1 - | head -c 64000 | wc -c   # 64000 while you speak
claude                                       # then /voice and speak
```

Record the result here: whether `/voice` transcribed, the remote OS, Claude Code version and which recorder it
used (`claude --debug` logs `[voice]` lines).
