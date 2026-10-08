# Project switching: what was tried, why it's hard, what's needed

**Status:** not shipped. `main` was rolled back to the v0.3.0 codebase (tagged
`v0.5.1`) after two successive redesigns each fixed their target problem but
introduced new, subtle failure modes. This document exists so a future attempt
doesn't have to rediscover the same things the hard way.

## Before picking this back up

This got close. The mechanics mostly worked - workspace-native switching
switched instantly with no pane shuffle, the dedicated instance genuinely
isolated fleet from the user's own WezTerm, and even the last feature
attempted (opening a project directly in whatever window `fleet` was invoked
from) worked at the pane/window level once its bugs were found. What was
missing was never really "can this be built," it was "build it 100%
correctly across every code path that touches a workspace or a driver
choice" - see "why this is hard, structurally" below - and that is
genuinely difficult, not a small follow-up.

Separately, and more importantly for whoever resumes this: the exact
intended behavior of "open a project in the current window" went through
several rounds of the user correcting a wrong guess during this session
(does "current window" mean fleet's own window, or literally any window;
does the picker distinguish lowercase/uppercase or Enter/Shift+Enter; should
the calling pane be closed or reused after opening; should the dashboard's
other actions - hide, dispatch - behave differently depending on where the
project lives). Each answer changed the design in a real way. That strongly
suggests the requirement was never fully pinned down on either side before
implementation started.

**Do not resume implementation from this document's design as if it were a
finished spec.** Ask the user, in detail, how each of the following should
actually work before writing any code:

- What "open in the current window" is *for* - what workflow does it serve
  that opening in fleet's dedicated instance doesn't?
- Whether hide/dispatch/other dashboard actions should be available at all
  for a project opened this way, given they rely on workspace-switching
  mechanics that are only safe inside fleet's own dedicated process (see
  point 2 under "what a real fix would need" below).
- Whether the calling pane should be closed, reused, or left alone after
  opening - and whether that answer depends on where the pane lives (inside
  fleet's instance vs. the user's own).
- Whether this is worth the added complexity at all, versus the simpler
  alternative noted in point 4 below (keep every project inside fleet's own
  instance, solve the "I want this from my regular terminal" need with a
  lightweight launcher instead of actually hosting panes there).

The rest of this document is a record of what was built, what broke, and why
- useful context for that conversation, not a substitute for having it.

## The problem this was trying to solve

Switching between open projects in fleet (pre-v0.4.0) was slow and visually
janky: 15-20 sequential `wezterm cli` subprocess calls per switch, moving and
retitling panes one at a time (`MoveProjectHandler`/`HiddenNest`). A naive fix
(parallelizing the external calls) corrupted panes and was reverted outright -
`wezterm cli` mutations are not safe to run concurrently against the same mux.

The real fix looked obvious: WezTerm has a native **workspace** primitive.
Activating a workspace natively hides/shows an entire window set instantly -
no pane surgery, no flicker. Fleet already shipped the Lua-side hook for it
(`fleet.lua`'s `user-var-changed` handler reacting to a `fleet-workspace`
signal); it was just never wired up from the C# side.

## Attempt 1: workspace-native switching (v0.4.0, v0.4.1)

Freshly-opened projects got tagged with their own WezTerm workspace
(`SpawnOptions.Workspace = project.Name`). Switching to such a project became
one `SwitchToWorkspace` call instead of pane-by-pane moves. This worked, and
worked well, in isolated manual testing - "it switched instantly, no pane
shuffle at all."

**What broke, in production, after shipping:** WezTerm workspace visibility is
**global to the entire GUI process, not per-window**. Switching workspaces
doesn't just change what one window shows - it swaps the contents of *every*
window that process owns. So the moment fleet activated a project's workspace,
it hid every other window in that WezTerm process, including windows that had
nothing to do with fleet. Symptom as reported: "the window where I open fleet
in will consume the other wezterm window and after I can't open others."

This is fundamental to WezTerm's workspace model, not a bug fixable with
better Lua scoping. (A real, separate bug *was* found and fixed alongside it -
`user-var-changed` fires once per open window with a shared `pane` argument,
not necessarily that window's own pane, so acting on it without checking
ownership could pull an unrelated window into a switch too. Fixed with an
`owns_pane` guard. That fix was correct but did not and could not address the
workspace-visibility problem above.)

**Lesson:** "hide" cannot be represented as "a different tab in the same
visible window" either - all tabs of a visible window are equally reachable
via the tab bar, so there's no such thing as a background tab distinguishable
from a normal one. This bit a mid-course design (folding hidden agents into a
background tab) before it was caught and corrected to use a real, separate
(but still same-process) workspace instead.

## Attempt 2: fleet's own dedicated, isolated WezTerm process (v0.5.0)

Given workspace visibility is global to a *process*, the fix was to give
fleet its own dedicated WezTerm GUI process - entirely separate from
whatever WezTerm the user runs for their own work - so fleet's
workspace-switching could never reach outside fleet's own control.

Mechanism: `config.unix_domains` with a custom `socket_path` +
`config.default_domain` + `config.default_gui_startup_args = { 'connect',
'<domain>' }` (all three - `default_domain` alone leaves the GUI client
connecting then exiting without ever showing a window, confirmed by direct
testing) gives a WezTerm GUI process its own isolated mux domain, backed by a
genuinely separate `wezterm-mux-server.exe` daemon with zero shared state.
Fleet stopped wiring anything into the user's personal `wezterm.lua` at all,
and auto-launched its own instance transparently on first use.

This was implemented, unit-tested (900 tests), and verified against a real
empirical spike before shipping (confirmed: a new OS process, not a reused
one; confirmed: `default_gui_startup_args` is required, not optional;
confirmed: unix domain socket paths have a real `SUN_LEN` length limit and a
deeply-nested scratch path was rejected outright; confirmed: `--domain-name`
is not a real `wezterm cli` flag, despite an earlier web search suggesting
otherwise - only `WEZTERM_UNIX_SOCKET` pinning actually works).

**What broke, after shipping, discovered while testing a follow-up feature:**

1. **`list` vs `list-clients`.** The auto-start "is fleet's instance already
   running" check used `wezterm cli list`, which only proves the mux
   *daemon* is alive - not that any GUI window is actually displaying it.
   `wezterm-mux-server.exe` persists independently of the GUI process that
   spawned it (by design, for reattach/detach). So once the original
   dedicated-instance window was closed, later `fleet` invocations found the
   daemon "reachable," skipped auto-launch, and silently created invisible
   panes nobody could see. Fixed by switching the check to `list-clients`
   and testing for a non-empty connected-client array.

2. **Dual-driver inconsistency was the deeper, unresolved problem.** A later
   feature ("open a project directly in whatever window `fleet` was invoked
   from, not fleet's dedicated one") needed a second driver path: an
   *ambient* `WezTermCli` (no pinned socket, trusts whatever
   `WEZTERM_UNIX_SOCKET` the calling shell already has) alongside the
   existing *pinned* driver for fleet's dedicated instance. The picker
   (`PickProjectCommand`) was updated to choose between them correctly. But
   the **dashboard TUI that runs inside every opened project**
   (`DashCommand`) had its own, separate, unconditional
   `Adapters.Mux(log)` call - fleet's dedicated-instance driver, always,
   regardless of where the project itself had been opened. Since nothing
   else had started that instance yet, the dashboard's own startup silently
   cold-started a whole second, unwanted WezTerm window as a side effect of
   the dashboard just initializing - even though the actual project panes
   had correctly landed in the user's own window the whole time.

   This was hard to find: `wezterm cli list --format json` against the
   *correct* window showed everything landing exactly right (one window,
   one new tab, both panes). The second window was a completely separate
   process, invisible to that query, discovered only by cross-referencing
   `Get-Process wezterm-gui` before/after and matching the extra PID's
   command line back to fleet's dedicated-instance config file.

   Fixing the one call site found (`DashCommand`) was mechanical. The real
   problem is structural: **every place that constructs a mux driver has to
   independently get the ambient-vs-dedicated decision right**, and nothing
   in the architecture enforces or even surfaces that requirement. `hide
   agent`, `dispatch`, and other dashboard actions still rely on workspace
   switching internally (`ProjectWorkspace`/`HiddenNest`) - mechanics that
   are safe *only* inside fleet's own dedicated process, for exactly the
   reason attempt 1 above exists. If any of those actions run against the
   ambient driver (a project opened "in the current window"), they would
   reintroduce attempt 1's original bug - hiding the user's own unrelated
   windows - just newly scoped to whichever action triggers it. This was
   identified but not fixed or fully audited before the decision was made
   to roll back instead.

3. **The chord regression.** Stopping all wiring into the user's personal
   `wezterm.lua` (fix for the process-isolation problem) had a side effect
   nobody had connected until a user noticed it directly: the `Ctrl+Enter`
   chord that used to open fleet's menu from *any* WezTerm window stopped
   working there entirely, because the Lua that binds it
   (`wezterm_keybinds.lua`'s `M.apply`) was only ever `require`d by fleet's
   own dedicated instance's config from then on. A trimmed, workspace-free
   variant of that module was designed and built (only the chord + a
   pane-close guard, none of the workspace-switching/notification
   machinery that made re-wiring the full module unsafe), but this was
   built *in response to* discovering the regression, not anticipated by
   the original design - another sign the full surface area of "what
   depends on being inside fleet's own instance" was not enumerated before
   the redesign shipped.

4. **A process-management near-miss, unrelated to the design itself but
   worth recording:** while cleaning up a scratch test rig during this
   work, a `taskkill` targeted at what was believed to be a throwaway
   process instead killed the daemon hosting the user's real, active
   session. Recovered by manually reconnecting a GUI client; the underlying
   cause (why the real session ended up sharing a daemon with a scratch
   one) was never fully root-caused. Flagged here because it's a reminder
   that this class of work involves real, running state on the developer's
   own machine, not just code - mistakes here have a different cost than a
   bad unit test.

## Why this is hard, structurally

- **WezTerm's workspace primitive is scoped to a GUI process, not a window.**
  Anything that wants per-project "instant switch, no pane surgery"
  semantics has to either (a) accept that switching can hide unrelated
  windows in whatever process it runs in, or (b) guarantee every workspace
  switch happens inside a process fleet fully owns. (b) is what the
  dedicated-instance redesign chose, correctly - but it means **every mux
  operation that might switch a workspace must run inside that instance,
  with no exceptions**, which is a much larger invariant to hold than "fleet
  auto-launches its own window."

- **A single global driver-selection point isn't enough once more than one
  driver exists.** `Adapters.Mux(log)` used to be the only way any command
  reached WezTerm; once a second, ambient driver was introduced for one
  feature, *every* other call site that had ever called `Adapters.Mux(log)`
  became a place that could silently pick the wrong one, with no compiler
  or test signal pointing at which ones needed to change. This is a
  cross-cutting concern (which driver am I allowed to use, right now, in
  this process) that the current architecture has no way to express or
  enforce - it lives entirely in each call site's author remembering to
  check.

- **Ambient discovery and pinned discovery look identical until they
  aren't.** `WezTermCli` with no pinned socket transparently falls back to
  probing `gui-sock-*` files newest-first when `WEZTERM_UNIX_SOCKET` isn't
  set. That's convenient and matches fleet's original (pre-v0.4) behavior,
  but it means "which WezTerm instance am I actually talking to" is
  sometimes environment-dependent in ways that are easy to get right in a
  quick manual test and wrong the first time a stale socket file or a
  second running instance is present.

- **WezTerm GUI auto-start is a side effect, not a return value.** Nothing
  about calling `mux.Driver.ListPanesAsync()` looks like it might cold-start
  a new top-level window. `AutoStartDriver` makes that side effect implicit
  in *every* method on the driver it wraps, which is exactly what made the
  `DashCommand` bug invisible until someone was specifically diffing
  `Get-Process` output before and after.

## What a real fix would need

1. **One decision point, one enforcement mechanism.** Every place that can
   reach WezTerm should go through something that *cannot* silently pick
   the dedicated instance when it should have picked ambient (or vice
   versa) - e.g., thread the resolved driver through the whole call chain
   from the single point where the decision is made (as `PickProjectCommand`
   → `OpenProjectHandler` → `RestoreSessionHandler` already does), and make
   any *new* top-level entry point (like `DashCommand`, spawned as a
   separate process) receive that decision explicitly - via an argument or
   environment variable set by the parent - rather than re-deriving it.
   Re-deriving it independently is exactly what went wrong.

2. **A full audit of every workspace-switching operation**, not just
   "opening" a project: hide/show agent, dispatch, cleanup - anything that
   calls into `ProjectWorkspace`/`HiddenNest` - needs an explicit answer to
   "is this safe to run against the ambient driver," and the ones that
   aren't need to either be disabled/hidden when a project is running
   ambiently, or be redesigned to not need a workspace switch at all in
   that mode.

3. **A cheap way to empirically verify "which OS-level processes exist"
   as part of normal testing**, not just "which panes does `wezterm cli
   list` report." The dashboard auto-launch bug was invisible to the mux
   query and only found by diffing `Get-Process wezterm-gui` before/after.
   Any future work on this should build that check in from the start
   (e.g., a test helper or manual checklist item), not rediscover it.

4. **Decide, deliberately, whether "open in the user's own window" is a
   feature fleet wants to support at all**, given point 2 above. It may be
   simpler and safer to *not* support it - keep fleet's dedicated instance
   as the only place any project ever lives - and solve the "I want this
   accessible from my regular terminal too" need a different way (e.g., a
   lightweight launcher/focus command rather than actually hosting project
   panes outside fleet's own process).

## Where everything still lives

Nothing described above was deleted - `main` was moved back to the v0.3.0
tree (now tagged `v0.5.1`, since `fleet update` requires a strictly greater
version number to offer an update at all; the number does not imply new
functionality over `v0.5.0`). The prior work is reachable at:

- `v0.4.0` / `v0.4.1` - the workspace-native redesign.
- `v0.5.0` - the dedicated-instance redesign, as shipped.
- `feat/dedicated-wezterm-instance` - the dedicated-instance work's own
  branch history.
- `feat/workspace-native-projects` - the workspace-native work's own branch
  history.
- `feat/pick-current-window` - this session's further work on top of
  v0.5.0: open-in-current-window picking, the `DashCommand` ambient-driver
  fix, and the trimmed chord-only wiring. Includes the fixes described
  above for the bugs found along the way; does **not** include a fix for
  the "hide/dispatch still assume the dedicated instance" gap in point 2
  above, which was found but not addressed.
