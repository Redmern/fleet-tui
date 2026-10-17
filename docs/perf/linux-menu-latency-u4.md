# Linux menu latency after U4: iteration rate and cursor heartbeat

Measured on 2026-10-17 on the same Linux box as the baseline (`linux-menu-latency.md`). Two NativeAOT linux-x64
release binaries were built in the same session: **before** = `perf/menu-latency-harness` (89663f2e, U1),
**after** = `perf/tg-driver-config` (U4). Both link the same libghostty-vt.

## What changed

- `FleetUi.Start` sets `Application.MaximumIterationsPerSecond = 100` (`FleetUi.IterationsPerSecond`) before
  `Init`. `FLEET_TUI_ITERATIONS=<n>` overrides it for A/B runs. It is a plain static property set, no TG config.
- `CursorHeartbeat.Quiet(app)` (`src/Fleet/Ui/CursorHeartbeat.cs`) runs at the start of every main loop
  iteration. It clears the driver's cursor-needs-update flag when the cursor already written equals the one the
  focused view wants (hidden when nothing visible is focused). Terminal.Gui 2.4.17 never clears that flag itself,
  so `UpdateCursor` rewrote the cursor every iteration. Anything that really moves the cursor in that iteration
  (a key in a text field, a focus change, a redraw that parks a visible cursor) sets the flag again before
  `UpdateCursor` runs, so real moves are still written once.
- Not changed: `SizeDetection` (`ESC[18t` every 500 ms), TG's 20 ms input poll, the main loop internals.
  There is no idle drop back to 25/s: at 100/s the idle app CPU stayed at ≈ 1 % (below), so it was not needed.

## Key → frame (harness, 40 presses per screen, ms)

e2e is key written → first byte of the redraw. Under `--busy` trust e2e, not the app hop (see the baseline's open
questions).

| condition | screen | before p50 | before p95 | after p50 | after p95 |
|---|---|---|---|---|---|
| idle 120×35 | menu | 34.8 | 57.1 | **19.4** | **29.3** |
| | picker | 40.6 | 56.4 | **19.5** | **31.8** |
| | dashboard | 45.4 | 55.8 | **21.8** | **31.2** |
| idle 280×75 ¹ | menu | 28.6 | 55.1 | **21.1** | **28.6** |
| | picker | 40.5 | 59.7 | **19.6** | **31.9** |
| | dashboard | 46.9 | 64.7 | **30.8** | **38.9** |
| 280×75 + 6 busy + 8 dashboards ¹ | menu | 29.9 | 52.8 | **22.9** | **33.4** |
| | picker | 24.7 | 48.8 | **22.6** | **31.6** |
| | dashboard | 46.2 | 58.3 | **33.8** | **46.1** |
| `fleet menu` direct, all queries answered | menu | 22.6 | 50.1 | **16.4** | **25.2** |
| `fleet menu` direct, only `18t` answered | menu | 34.3 | 56.2 | **15.0** | **24.4** |

¹ "before" for these rows is the U1 baseline table (same box, same day), not a rerun.

Pass target (menu and picker e2e p95 < 35 ms) is met in every condition. The inside-fleetd app hop for the idle
menu went from p50 23.2 / p95 49.3 to 14.8 / 25.4, and the render hop stayed at 4.2 ms.

### A/B: is the rate the cause? (AC-7)

Same U4 binary, `FLEET_TUI_ITERATIONS=25` (heartbeat fix still on), idle 120×35:

| screen | 25/s p50 | 25/s p95 | 100/s p50 | 100/s p95 |
|---|---|---|---|---|
| menu | 37.3 | 50.2 | 19.4 | 29.3 |
| picker | 41.5 | 56.3 | 19.5 | 31.8 |
| dashboard | 41.6 | 57.7 | 21.8 | 31.2 |

At 25/s the numbers are back at baseline, so the gain comes from the iteration rate and the heartbeat fix alone
does not change latency.

### Menu open (AC-6)

Warm menu first frame (`FLEET_MENU_TIMING`): before 4.0 ms; after 4.0 ms idle, 4.0–5.0 ms at 280×75, 6.0 ms
loaded (baseline loaded range was 2–6 ms). No regression.

## Idle writes

### Write syscalls per process (`/proc/<pid>/io` `syscw`, 10 s idle)

Real fleetd at 120×35, the dashboard tab, 2 extra hidden `fleet dash` panes, and the menu float open, all idle.

| process | before | after (100/s) | after at 25/s |
|---|---|---|---|
| `fleet menu` | 53.2/s | **2.0/s** | 2.0/s |
| `fleet dash` (visible) | 91.8/s | 40.6/s | 40.6/s |
| `fleet dash` (hidden, ×2) | 111/s | 59.9/s | 59.8/s |
| fleetd | 7.8/s, 1.8 % CPU | 8.0/s, 0.4 % CPU | 7.8/s, 0.5 % CPU |
| each TG app CPU | 0.4–0.8 % | 0.8–1.2 % | 0.5–0.7 % |

- The menu's remaining 2/s is the `ESC[18t` size poll (twice a second), which this unit leaves alone.
- The dashboards' remaining writes are real repaints, not the heartbeat. Run directly in a pty, `fleet dash`
  sends ≈ 2.7 KB/s of frame content idle (≈ 16 reads/s, rows redrawn from `ESC[1;1H`) both before and after.
  Before, it also sent 24.6 cursor-only heartbeats/s; after, ≈ 0 (1.8/s left are frame fragments the
  heartbeat regex matches). The idle dashboard repaint is a separate finding for U2.
- fleetd's CPU fell from 1.8 % to 0.4 %, because it no longer wakes on every heartbeat.

### Cursor-only heartbeats from `fleet menu` (harness `--direct`)

Before 24/s (at 25/s; U1 measured 109/s at 100/s without the fix). After 0/s.

## Cursor still works in text inputs

`fleet menu` → `n` opens the new-project form with the name field focused. Recorded raw pty bytes:

| step | before | after |
|---|---|---|
| form shown | `ESC[?25h` written | `ESC[5 q ESC[?25h ESC[4;13H` |
| idle 3 s in the form | 77 writes (`ESC[?25h` every iteration) | 0 writes besides `ESC[18t` |
| type `abc` | cursor shown | `ESC[?25h ESC[4;16H` (moved 3 columns) |
| resize 100×30 → 120×35 | cursor shown | `ESC[?25h ESC[4;16H` after the redraw |
| Tab to the next field | cursor shown | `ESC[?25h ESC[6;13H` |

## Not measured

- Windows. The change there is the same two settings. `WindowsOutput.SetCursor` has the same "write
  every time" shape, so the heartbeat fix only removes no-op writes; the rate change is platform-neutral.
  Run the harness probe there (`FLEET_KEY_TIMING=1`) to confirm.
- The real Ghostty terminal (the harness pty stands in for it, as in U1).

## How to rerun

```bash
dotnet publish src/Fleet/Fleet.csproj -c Release -r linux-x64 -o out/linux-x64
K() { python3 scripts/e2e/keylatency.py run --presses 40 "$@"; }
K out/linux-x64/fleet --direct
K out/linux-x64/fleet --cols 280 --rows 75
K out/linux-x64/fleet --cols 280 --rows 75 --busy 6 --dashes 8
FLEET_TUI_ITERATIONS=25 K out/linux-x64/fleet      # A/B the rate on the same binary
```

The idle write and cursor checks were throwaway scripts on top of `keylatency.Fleet` and `keylatency.Terminal`:
sample `syscw` and `utime+stime` for every `bin/fleet` process twice 10 s apart, and for the cursor check set
`keylatency.is_heartbeat = lambda d: False` so cursor-only chunks are kept.
