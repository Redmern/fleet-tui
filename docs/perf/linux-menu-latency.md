# Linux menu navigation latency: harness and baseline

Measured on 2026-10-17 on the maintainer's Linux box (24 cores, Omarchy, kernel 7.2), with fleet built from
`perf/menu-latency-harness` (0.7.19 plus the probe) as a NativeAOT linux-x64 release binary with libghostty-vt linked.

## Headline

- **Most of each keypress goes to Terminal.Gui's own pacing, not to fleetd.** Typical key→frame is p50 ≈ 30–40 ms
  and p95 ≈ 55–60 ms on every screen.
  - The time inside the app (key reaches the pane → the pane's frame-changing output) is p50 ≈ 24 ms and
    p95 ≈ 45–75 ms.
  - fleetd's render loop adds a flat ≈ 4.2 ms, which is `FrameDelay`.
- That pacing is two fixed waits: the 40 ms main loop (`MaximumIterationsPerSecond = 25`) and the 20 ms input poll.
  A keypress waits for the next tick, which gives 20–60 ms of uniform delay. With iterations raised to 100/s
  (experiment only), menu p95 drops from 56 ms to 28 ms.
- **The dashboard's 1.5–4 s app-hop maxima are a harness artifact, not UI-thread stalls.** The opener keys
  `C-s` and space are recorded against the focused dashboard pane, but fleetd consumes them to open the menu.
  They stay pending until that pane's next output, the redraw for the first measured `l`. U2 confirmed this with
  a stall probe: the dashboard's UI thread never gapped more than 150 ms, idle or loaded
  (`docs/perf/linux-menu-latency-u2.md`; the probe was throwaway, not committed). The loaded app-hop p95 of ≈ 100–200 ms is the
  same kind of misattribution under `--busy`. Loaded e2e first-byte stays at p95 ≈ 50–60 ms.
- **Idle Terminal.Gui apps never go quiet.** Every loop iteration writes `ESC[?25l ESC[1;1H` (hide cursor,
  home) even when nothing changed, plus `ESC[18t` every 500 ms. In the user's real fleet, the 2 dashboards make
  ≈ 300 write syscalls/s and the warm menu ≈ 53/s, idle. Each write wakes fleetd's render loop (snapshot and
  compose for every session). That costs CPU rather than key latency today: fleetd sat at ≈ 2 % CPU. It is also
  a plausible Windows/Linux difference, because ConPTY swallows cursor-only no-ops and a Linux pty passes them
  through.
- I could not reproduce "very slow" (hundreds of ms on every key) in an isolated fleetd. The worst sustained
  number is the loaded dashboard (p95 ≈ 200 ms). If users still see it, the likely sources are the dashboard
  load on the app process and the 60 ms worst case felt on every key of a held arrow. See the open
  questions.

## Results

All numbers are ms, for 40 single presses per screen, alternating Down/Up. The dashboard uses `l`/`h` (next
and previous tab). The harness waits for the screen to go quiet, plus 0–60 ms of random jitter, before each
press.

- **e2e** is from the key written into the harness pty to the first byte of the redraw back from `fleet`.
- **app** is from the key reaching fleetd to the focused pane's frame-changing output.
- **render** is from that output to the frame fleetd composes.

### A/B conditions

| condition | screen | e2e p50 | e2e p95 | app p50 | app p95 | render p50 | render p95 |
|---|---|---|---|---|---|---|---|
| idle 120×35 (baseline) | menu | 41.9 | 55.5 | 24.2 | 49.7 | 4.2 | 4.3 |
| | picker | 36.3 | 56.4 | 23.9 | 45.6 | 4.2 | 4.3 |
| | dashboard | 39.1 | 57.1 | 34.5 | 74.3 | 4.2 | 4.4 |
| idle 280×75 | menu | 28.6 | 55.1 | 21.3 | 42.7 | 4.4 | 4.6 |
| | picker | 40.5 | 59.7 | 25.7 | 47.8 | 4.4 | 4.6 |
| | dashboard | 46.9 | 64.7 | 37.4 | 68.8 | 4.6 | 4.9 |
| 280×75 + 6 busy panes + 8 extra dashboards | menu | 29.9 | 52.8 | 23.8 | 101.0 | 4.1 | 7.2 |
| | picker | 24.7 | 48.8 | 23.5 | 72.7 | 3.8 | 7.0 |
| | dashboard | 46.2 | 58.3 | 38.4 | 204.9 | 5.4 | 7.5 |
| 120×35 + 6 busy panes ¹ | menu | 23.6 | 59.0 | 19.9 | 48.9 | 4.1 | 4.8 |
| | dashboard | 23.3 | 61.2 | 25.1 | 71.1 | 3.5 | 4.6 |
| driver forced to `dotnet` ¹ | menu | 21.7 | 61.0 | 17.8 | 55.5 | 4.2 | 4.3 |
| | dashboard | 22.1 | 59.9 | 18.8 | 55.2 | 4.2 | 4.4 |
| **experiment: 100 iterations/s** | menu | 20.8 | 28.0 | 15.9 | 25.9 | 4.2 | 4.3 |
| | picker | 18.6 | 31.1 | 14.4 | 25.4 | 4.2 | 4.3 |
| | dashboard | 22.1 | 29.8 | 22.8 | 58.8 | 3.2 | 4.3 |

¹ These runs predate the press jitter. Their p50 is phase-locked low, so compare only their p95 and max.

### Holding a key

The harness sends 25 presses at 30 Hz, then measures from the last press until the screen is quiet. Typical
results are 18–60 ms on every screen and condition: there is no backlog. Terminal.Gui drains every queued key
in one iteration.

### Outliers worth chasing

- **Dashboard:** the app hop has a max of 1556–4026 ms in every run, idle included. This is not a stall. The
  menu opener keys (`C-s`, space) are timed against the dashboard pane, which never answers them, so they
  resolve only with the first measured `l`. The e2e max for the same presses is ≈ 60 ms, and U2's stall probe
  found no UI-thread gap over 150 ms. To read the dashboard app hop, skip the opener keys or read e2e beside it.
- **Picker:** the render hop has a single ≈ 420–470 ms max in every run. That is fleetd holding the freshly
  opened float back (reveal gating) while the first key arrives. It happens once per open, not per key.

### `fleet menu` outside fleetd (`--direct`)

| terminal queries answered | e2e p50 | e2e p95 |
|---|---|---|
| all (DA1, CPR, 18t, OSC 10/11, kitty) | 37.2 | 51.5 |
| only `ESC[18t`, as fleetd does | 28.9 | 54.3 |

- Outside fleetd, latency matches the in-fleetd numbers less the ≈ 4 ms render hop.
- Leaving queries unanswered changes nothing for navigation.
- The menu writes CPR, DA1, OSC10, OSC11, kitty `ESC[?u` and `ESC[18t` at startup.
- Idle, it writes 24 cursor-only heartbeats per second at 25 iterations/s, and 109/s at 100 iterations/s.

### Menu open (AC-6)

The warm menu's first frame stays at 2–6 ms (`FLEET_MENU_TIMING`) in every condition. The probe is off by
default. With it off, the only change on the product path is a null-conditional call on a null field, and
`Init(null)` when `FLEET_TUI_DRIVER` is unset.

## Hypotheses

| # | hypothesis | verdict | evidence |
|---|---|---|---|
| 1 | Terminal.Gui stalls on terminal queries fleetd does not answer | **disproven** for navigation | `--direct`: answering every query vs only `18t` gives the same p95 (51 vs 54). Queries only gate startup (`AnsiStartupGate`, ≤ 1 s per query). Forcing the `dotnet` driver changes nothing. |
| 2 | Per-key sync fit/hold/float-buttons on the UI thread hit reveal gating | **disproven** per key | The log shows `fit` and `revealed` only on opens. Render hop p95 ≤ 4.4 ms idle. Reveal gating costs ≈ 450 ms once per picker open. |
| 3 | The serial render loop scales with busy panes | **disproven** at this scale | Render hop stays at ≈ 4.2 ms, p95 ≤ 7.5 ms, with 6 busy panes and 8 extra dashboards at 280×75. |
| 4 | The theme watcher redraws repeatedly | **disproven** | inotify saw 4 theme-file events per run, all at startup. `ThemeWiring.Follow` redraws only when the theme text changes. |
| 5 | libghostty snapshot/lock contention | **disproven** at this scale | Same render-hop evidence as #3. |
| — | **Terminal.Gui loop pacing (40 ms iteration + 20 ms input poll)** | **proven, the main share** | App hop spreads evenly over 0–40 ms with a bump at ≈ 55 ms. 100 iterations/s halves p50 and p95. Source: `ApplicationMainLoop.Iteration` sleeps the rest of the 40 ms; `InputImpl` polls, then `Task.Delay(20)`. |
| — | **Dashboard UI-thread stalls** | **disproven** (harness artifact) | The seconds-long app-hop outliers are the menu opener keys, pending until the dashboard's next output. e2e max ≈ 60 ms for the same presses. U2's stall probe saw no UI-thread gap over 150 ms, idle or loaded. |
| — | **Idle cursor heartbeat** | **proven** (CPU, wakeups) | `ApplicationNavigation.UpdateCursor` runs every iteration: `GetCursorNeedsUpdate()` is never reset, and `AnsiOutput.SetCursor` writes hide plus `1;1H` without comparing to the last state. |

## Recommendation: what to run next

1. **U4, Terminal.Gui driver and iteration config. Do this first.** It is the largest measured share and the
   change is small.
   - Raise `Application.MaximumIterationsPerSecond`, or better, wake the main loop when input arrives instead
     of sleeping the rest of the 40 ms.
   - Pair it with stopping the idle cursor heartbeat: reset `Driver.SetCursorNeedsUpdate(false)` after a draw,
     or skip the write when the cursor state is unchanged. Otherwise 100 iterations/s means 4× the idle writes
     and fleetd wakeups.
   - Consider `Driver.SizeDetection = Polling` to drop the 500 ms `ESC[18t`.
   - Expected gain: p95 55–60 ms → under 30 ms on every screen, which the experiment shows.
2. **U2, UI-thread per-key work and redraws.** Confirm no fleet-side work runs per key and look for redraws
   that change nothing. The dashboard's 1.5–4 s app-hop maxima turned out to be a harness artifact (see the
   outliers above), not stalls. Re-measure with `--dashes`/`--busy` after U4.
3. **U3, daemon render loop. Last.** The render hop is a flat ≈ 4 ms (`FrameDelay`) and does not grow with load
   here. Its only measured cost is waking on every heartbeat write, which U4 removes at the source.
   - Optional: lower `FrameDelay`, or ignore output that leaves a pane's screen unchanged.
   - Optional: make the ≈ 450 ms first-key reveal hold on picker opens shorter.

## Open questions and gaps

- **Visible busy panes.** When a visible pane changes every frame, a cursor-only heartbeat on the key's pane can
  count as its answer. In that case trust the e2e first-byte numbers, not the app/render split. The busy panes
  in these runs are hidden tabs, so they are unaffected.
- **"frame done" under `--busy`.** It is unreliable there (≈ 460–890 ms), because unrelated bar updates keep the
  output busy. Use first byte.
- **The real Ghostty terminal and Windows were not measured.** The harness pty stands in for Ghostty: the attach
  client sends no terminal queries and Linux keys arrive as raw bytes, as from Ghostty. To compare Windows, run
  the same probe there with `FLEET_KEY_TIMING=1` and read the log with `keylatency.py log`.

## How to rerun

```bash
bash scripts/ghostty/build.sh                      # once: libghostty-vt for the AOT binary
dotnet publish src/Fleet/Fleet.csproj -c Release -r linux-x64 -o out/linux-x64
python3 scripts/e2e/keylatency_test.py             # harness unit tests

K="python3 scripts/e2e/keylatency.py run out/linux-x64/fleet --presses 40"
$K --direct                                        # baseline + fleet menu outside fleetd (all vs fleetd-only answers)
$K --cols 280 --rows 75                            # full-screen size
$K --cols 280 --rows 75 --busy 6 --dashes 8        # loaded: 6 hidden busy panes, 8 extra idle dashboards
$K --driver dotnet                                 # force the Terminal.Gui driver (FLEET_TUI_DRIVER)
```

`run` starts its own fleetd under `/tmp/fleet-keylatency` (its own socket, config and stand-in claude/nvim). It
never touches the user's fleetd.
- Its log is `/tmp/fleet-keylatency/cfg/fleet.log`, and `screen-<name>.txt` there shows what each screen drew.
- To split any fleet log into hops: `python3 scripts/e2e/keylatency.py log <fleet.log> [--panes p3,p4]`.
- On a real session, set `FLEET_KEY_TIMING=1` in fleetd's environment. It logs
  `key timing <client> <pane>: output X ms, frame Y ms` per key.

The 100 iterations/s experiment was a throwaway build that set `Application.MaximumIterationsPerSecond = 100`
before `Init`. It is not committed.
