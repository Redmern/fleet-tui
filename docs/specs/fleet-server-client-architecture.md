# Spec — the machine you sit at leads

Status: **revision 2, approved at the Plan gate on 2026-11-13.** Revision 1 was approved on 2026-11-12;
planning against the code forced the changes listed under "Changes since revision 1".
Base: `origin/main` @ `8c3bb380`, fleet 0.7.29. Background: `discover.md`, `reports/prior-art.md`, `design.md`.

## Problem

Every fleet can link to every other fleet, and a remote project is drawn by the remote fleet, menu
included. So inside a project on machine B, the fleet menu is B's: **Remote machines** shows B's links
(none) and B's `remotes.json`, while the same screen from a local project shows yours. Theme, keybinds,
update, logs and save session in that menu also silently mean B. Nothing prevents chains either: from
inside B's menu you can link B to C, and B's view of C already shows up in your project list.

Two more facts make it worse:
- A second way to view a remote (`SHIFT` in Switch project, or `fleet attach --ssh host`) gives B's own
  everything, including B's switcher, machine list and head.
- The rule "everything goes through the machine fleet was opened on" already exists, but only for the head.

## Decision

One rule, no installed roles:

> **The machine you sit at owns the machine list, the head, sessions and fleet's own screens.
> A remote machine serves its own projects, and nothing else, to whoever views it.**

- Same install everywhere. A box you sit at directly is a full fleet and may link out.
- A machine reached through a link never shows, uses or creates links of its own for that viewer. Chains
  are impossible, not just discouraged.
- The remote keeps drawing its project. Its menu screens in a remote view are split into labelled groups.
- Words stay as they are: **this machine** and **remote machines**. No "server", "client", "hub", "node".

Decided with the user:

| # | Question | Answer |
|---|---|---|
| D-1 | How a machine gets its role | By where you sit; no setting, one install |
| D-2 | What a remote project view shows | Remote draws the project; this machine owns the rest |
| D-3 | Deliverable | Spec, plan, then build |
| D-4 | Remote's log, version, update | Menu group `MACHINE <name>` |
| D-5 | ssh window (`SHIFT`, `attach --ssh`) | Project-only window |
| D-6 | Theme follows into remote views | Not in this task (todo 0004) |
| D-7 | Install over ssh, always-on service | Not in this task (todo 0005) |
| D-8 | Settings in a remote view | Both, labelled by machine |
| D-9 | Wording | this machine / remote machines |
| D-10 | Menu shape (2026-11-13) | Groups on each existing screen; FLEET has one Settings row that opens this machine's own settings screen |
| D-11 | Name in group titles (2026-11-13) | Your nickname; the hostname without one and in an ssh window |

## Terms

- **This machine**: the machine whose keyboard and screen you use.
- **Remote machine**: a machine in this machine's list (`remotes.json`).
- **Remote view**: a window on this machine showing a remote project in place (`project @machine`).
- **ssh window**: a terminal window attached straight to a remote fleet (`SHIFT` in Switch project, or
  `fleet attach --ssh host`).
- **Remote viewer**: a remote view or an ssh window, seen from the remote machine. Also anything the remote
  machine runs on that viewer's behalf, such as the menu it opened.
- **`<machine>`**: in a remote view, the nickname this machine gave the remote, or the remote's own machine
  name when it has none. In an ssh window, always the remote's own machine name.

## Scope

1. The menu in a remote view: labelled groups on each screen, each naming its machine.
2. Fleet-level screens always resolve to this machine in a remote view.
3. Chains refused: a remote viewer cannot list, make, answer, drop or show links on the machine it views,
   and never sees that machine's views of other machines.
4. The ssh window becomes a project-only window.
5. README and DESIGN state the rule.

### Menu screens in a remote view

```
ctrl+enter inside  homelab-api @homelab            Settings (under MACHINE homelab)

  PROJECT  on homelab                               PROJECT  on homelab
    quit · dashboard · agents · editor               open port · rebuild dashboard
    todo · files                                     clean up · hide all agents

  MACHINE  homelab                                  MACHINE  homelab
    settings ›                                       fleet config › · view log
                                                     update fleet · version · what's new
  FLEET    this machine
    switch project · notifications
    remote machines · save session
    settings ›   → this machine's settings screen, drawn by this machine:
                   save session · remote machines · keybinds · show keybinds
                   button hints · theme · view log · update fleet · version · what's new
```

| Screen | Drawn by | Groups |
|---|---|---|
| Main | remote | PROJECT, MACHINE (one row: Settings), FLEET (Switch project, Notifications, Remote machines, Save session, Settings) |
| Settings (remote's) | remote | PROJECT, MACHINE |
| Fleet config (remote's) | remote | unchanged; it is the remote's |
| This machine's settings | this machine | one list, all acting on this machine |

No row appears twice on a screen and no existing key moves.

### Which machine an action runs on in a remote view

This table is about ownership. The menu groups the rows it has today; dashboard keys and the head chord
are not menu rows and follow the same ownership.

| Owner | Actions |
|---|---|
| Remote, the project | agents (new, list, harness, stop, remove, hide), focus main, repositories, files, editor, todo board, project permissions, clean up, rebuild dashboard, remove project, web app, open port, quit |
| Remote, the machine | its settings and fleet config (models, default harness, AIDLC mode, Claude profile, auto-close, editor, orchestrator instructions, nvim config), its log, version, what's new, update fleet |
| This machine | switch project, remote machines, notifications, save session, the head, theme, keybinds, show keybinds, button hints, and this machine's own log, version, what's new and update fleet |

Key chords for view log, update fleet, version and what's new keep meaning the remote in a remote view.
This machine's are on this machine's settings screen.

## Non-scope

- This machine drawing the chrome (tab bar, menu, dashboard) of remote projects, Herdr-style (todo 0006).
- Your theme following you into remote views (todo 0004).
- Installing fleet on a remote over ssh; running fleetd as an always-on service (todo 0005).
- A role setting, a second binary or a second install.
- `machine/project` addressing in the CLI or new MCP tools. Head tools keep their `remote` parameter.
- A new field in the wire Hello. Old fleets are handled per operation, as today.
- More than one window following the same remote machine in place.
- A project switcher inside an ssh window.
- `docs/host-sync-wire.md` / `sync_to_remote`.
- A version bump or release.

## Acceptance criteria

Remote view: the menu

- **AC-1** When a fleet menu screen that the remote draws opens in a remote view, the fleet menu shall show
  its rows under the group titles `PROJECT on <machine>`, `MACHINE <machine>` and, on the main screen,
  `FLEET this machine`, with the rows of each screen grouped as in "Menu screens in a remote view".
- **AC-2** When the fleet menu opens in a project on this machine, the fleet menu shall show the same
  rows, keys and layout as before this change, with no group titles.
- **AC-3** When a row under `FLEET this machine`, or a row on this machine's settings screen, is chosen in
  a remote view, fleet shall run it on this machine, against this machine's config, `remotes.json`,
  sessions, log and binary.
- **AC-4** When **Remote machines** is opened from a remote view, by the menu or by its key chord, fleet
  shall show this machine's remote machines, with the same rows and states as when it is opened from a
  project on this machine.
- **AC-5** When **Save session** is chosen in a remote view, by the menu or by its key chord, fleet shall
  save the session on this machine, with the remote project recorded together with its host.
- **AC-6** When a row under `PROJECT on <machine>` or `MACHINE <machine>` is chosen in a remote view, or
  the key chord for view log, update fleet, version or what's new is used there, fleet shall run it on the
  remote machine, as it does today.
- **AC-7** When Switch project, Notifications or the head chord is used in a remote view, fleet shall open
  this machine's switcher, notification center or head, as it does today.
- **AC-20** When **Settings** under `FLEET this machine` is chosen in a remote view, this machine shall
  draw its own settings screen with save session, remote machines, keybinds, show keybinds, button hints,
  theme, view log, update fleet, version and what's new, each acting on this machine.
- **AC-21** When this machine has a nickname for a remote machine, the tab bar of a remote view of that
  machine shall read `project @<nickname>`.

No chains

- **AC-8** When a remote viewer asks a fleet to list, connect, answer, disconnect or show a remote machine,
  to create a project on one, to read or dismiss its notices, or to send it a head tool, that fleet shall
  refuse with a message saying that remote machines are managed from the machine you sit at, and shall not
  open an ssh connection or change its `remotes.json`.
- **AC-9** When a fleet lists its projects or workspaces for a remote viewer, it shall list only projects
  whose folder is on that machine, never a view of another machine.
- **AC-10** When a remote viewer asks a fleet to show a view of another machine, or would be placed on one
  when it attaches or restores a session, that fleet shall refuse or skip it.
- **AC-11** When a head tool arrives over a link with a `remote` other than `local`, the remote fleet shall
  refuse it, as it does today.
- **AC-12** When fleet is used directly on a machine, **Remote machines** there shall show and manage that
  machine's own list, and its head shall reach those machines, whether or not another machine is viewing it
  at the same time.

ssh window

- **AC-13** When the fleet menu opens in an ssh window, the fleet menu shall show `PROJECT on <machine>` and
  `MACHINE <machine>` only, and one line saying that fleet's own screens are in the window on the machine
  you sit at.
- **AC-14** When Switch project, Notifications, Remote machines, Save session or the head chord is used in an
  ssh window, fleet shall not open the remote's switcher, notification center, machine list, session or
  head, and shall show the same one-line hint.
- **AC-15** When `SHIFT` opens a remote project from Switch project, fleet shall open an ssh window showing
  that project, as it does today.

Compatibility

- **AC-16** When the remote fleet is older than this change, a remote view shall still show the project;
  Switch project, Notifications and the head shall still open on this machine; and the key chords for
  Remote machines and Save session shall act on this machine without reaching the remote.
- **AC-17** When this fleet is older than the remote fleet, the remote fleet shall still serve the project
  to it, shall still refuse the operations in AC-8 and AC-10, shall still hand Switch project and
  Notifications back to it, and shall show the one-line hint for any other row under `FLEET this machine`.
- **AC-18** When ISO mode is on, the remote viewer rules of ISO mode shall apply unchanged.

Docs

- **AC-19** When this change ships, `README.md` (Remote machines) and `docs/DESIGN.md` shall state the rule
  in the Decision section, describe the menu groups, this machine's settings screen and the ssh window, and
  no longer say that the fleet menu in a remote view is entirely the remote's.

## Assumptions

- **A-1** A remote viewer is recognised without a new wire field: a connection that came through
  `fleet bridge`, a client with a link label, or a request made on behalf of such a client (the menu the
  remote opened for it). A remote view has a label; an ssh window is bridged with none.
- **A-2** Multiplexer key chords in a remote view are already read with this machine's keymap. Keys inside
  the remote's menu and dashboard stay the remote's.
- **A-3** With a remote fleet older than this change, its menu is the old undivided one and can still reach
  its own Remote machines screen by a menu pick (not by chord, AC-16). The gap closes when the remote is
  updated.
- **A-4** Resolved in Plan: `fleet attach --ssh host` without `--project` runs no chooser; the remote shows
  its first project. That stays, except it is never a view of another machine (AC-10).
- **A-5** `WarmMenuLinkedTests.cs:102` sends `menu switch-project` over a link and must change, because that
  chord no longer travels. `RemoteLinkTests.cs:323` stays true (the menu still opens on the remote).
- **A-6** The body of the `fleet menu` process has no tests. The grouping and the "runs on which machine"
  decision live in `Shared` and `Features/Menu`, where tests reach them.
- **A-7** Existing `remotes.json` files on every machine stay valid and are not migrated.
- **A-8** A project that a local user and a remote viewer look at at the same time costs one extra menu
  redraw when the menu opens. A project seen only through the link opens its menu as fast as today.
- **A-9** A changed nickname shows in titles and tab bar at the next connect or project switch.
- **A-10** A remote's tab bar showing the nickname is new visible behaviour that follows from D-11.

## Open questions

None open. Deferred, by decision:

- **Q-1 (deferred, D-6, todo 0004)** Theme follows the viewer into remote views.
- **Q-2 (deferred, D-7, todo 0005)** Install fleet on a remote over ssh when connecting; always-on service.
- **Q-3 (deferred, todo 0006)** This machine draws all chrome (Herdr-style). Needs a decision map of its own.
- **Q-4 (deferred, in todo 0005)** Per-machine upkeep on the Remote machines screen.

## Changes since revision 1

| Item | Was | Now | Why |
|---|---|---|---|
| Menu layout, AC-1 | one flat list, three groups | groups on each existing screen (D-10) | five actions exist for both machines; a flat list doubles rows and clashes on keys `t` and `l` |
| AC-20 | — | this machine's settings screen | home of this machine's theme, keybinds, log, update, version |
| AC-21, `<machine>` | nickname assumed available | nickname sent over the link (D-11); tab bar shows it too | the remote does not know your nickname |
| AC-3, AC-5, AC-6 | menu only | chords named too | chords for owned screens stay here; log/update/version chords stay the remote's |
| AC-8 | included "forget" | "forget" removed; projects-on-remote, notices and head tools added | forget is not an operation, only a screen; the others reach other machines too |
| AC-10 | show only | also attach and session restore | a viewer could land on a view of another machine |
| AC-13 | `<machine>` unspecified | hostname in an ssh window | no nickname exists there |
| AC-16, AC-17 | degrade "as before" | chords act here against old remotes; old viewers get the hint | found possible, and prevents a silent drop |
| Group table | listed every action as a menu row | ownership table | several are dashboard keys or chords |
| A-1, A-4, A-5, A-6 | assumptions | corrected or resolved | checked against the code |
