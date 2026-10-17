# Linux menu latency, U2: dashboard stalls and idle repaint

Measured on 2026-10-17 on the same Linux box as the baseline (`linux-menu-latency.md`) and U4
(`linux-menu-latency-u4.md`). Two NativeAOT linux-x64 release binaries, built in the same session with the same
libghostty-vt: **before** = `perf/tg-driver-config` (2e5ea26e, U4), **after** = `perf/menu-ui-thread` (U2).

## Headline

- **The 1.5–4 s dashboard "stalls" are not stalls.** They are a measurement artifact of the harness's opener
  keys. The dashboard's UI thread never blocked for more than 150 ms in any run, idle or loaded. Nothing was
  changed for them.
- **The idle dashboard repaint is fixed.** Every 4 s refresh redrew the whole screen twice even when nothing
  had changed. An idle `fleet dash` now writes 2.3 times a second (the `ESC[18t` size poll) instead of 60, and
  15 B/s instead of 1.8 KB/s, visible or hidden.
- **Menu navigation does no fleet-side work per key.** There is no daemon request and no window redraw on
  Down/Up (now guarded by a test). What remains per key is Terminal.Gui's own pacing and output. TG writes one
  cursor-position escape, as its own `write(2)`, for every unchanged cell on a changed row: 567 syscalls and
  5 KB per menu key. Fleet cannot change that without replacing TG's output, which TG 2.4.17 does not expose.

## 1. Dashboard "stalls" of 1.5–4 s: a measurement artifact

**Hypothesis:** something on the dashboard UI thread blocks for seconds on some `l`/`h` tab switches.
**Verdict: disproven.** No fix.

- **Reproduced.** The harness on the before binary again showed the dashboard app hop at max 3988 and 4042 ms,
  idle, at 120×35.
- **The e2e numbers disagree.** For the same 40 presses, key → first byte of the redraw had max 32–35 ms. A
  4 s UI-thread block would show in both.
- **Where the number comes from.** The two outliers in the fleetd log
  (`key timing c1 p2: output 3987.9 ms` and `3187.9 ms`) are exactly 800 ms apart. That is the harness's
  `--step` between the opener keys `C-s` and space. fleetd records those two keys against the focused pane p2
  (the dashboard), but consumes them itself to open the menu. p2 sends nothing for them, so they stay pending
  until p2's next output, which is the redraw for the first measured `l`. All three resolve in the same
  instant, and the third, the real key, is 20.5 ms. The U1 maxima (1556, 3213, 4026 ms) vary with how long the
  menu stayed open before the first `l`.
- **The UI thread never stalled.** This was checked directly with a throwaway probe in every Terminal.Gui
  process (`FLEET_STALL_PROBE`, not committed). It logged any gap over 150 ms between main-loop iterations, and
  on a gap over 400 ms attached gdb for all stacks.
  - Idle run: no gap in `fleet dash` or any menu.
  - Loaded run (280×75, 6 busy panes, 8 extra dashboards): no gap in any of the 9 dashboards or the menus.
  - The only dump was the top-level `fleet` picker. It leaves its loop on purpose when it hands over to the
    attach client.
  - The probe's detection works: it caught that hand-over.
- **The loaded app-hop p95 of 100–200 ms (and its maxima) is the same kind of misattribution.** Under `--busy`,
  unrelated output answers pending keys, as the baseline's open questions say. Loaded e2e first-byte is p95
  44–46 ms, max 49 ms.
- **Fix for the harness, not done (not my unit):** count a key only when its pane is the one that answers, or
  skip prefix/opener keys. Until then, read the dashboard's app hop with e2e beside it.

## 2. Idle dashboard repaint: fixed

**Hypothesis:** the idle dashboard redraws although its data does not change. **Verdict: proven, fixed.**

**Evidence.** I captured `fleet dash` idle in a pty, timestamping every chunk, 10 s.
- Between refreshes it writes only `ESC[18t` every 500 ms.
- Every 4 s (`DashboardRefresh.Interval` → `Beat` → `AutoRefreshAsync`) it writes **two full frames of 6.8 KB
  each**, all 35 rows from `ESC[r;1H`, with identical content.
- Two frames because the repository bind and the pane binds arrive in separate `app.Invoke`s.
- Cause, on every bind, changed or not:
  - `FleetRows.Fill` installs a new `FleetRowSource`, so the list redraws.
  - `FleetTabBar.Retitle` re-arranges and calls `SetNeedsLayout`/`SetNeedsDraw`.
  - `FleetActionBar.Show` rebuilds the chips and calls `SetNeedsLayout`.
- That layout pass makes Terminal.Gui draw the window again. A redraw of any subview also redraws the window's
  adornments (border), so every row turns dirty (see §3).

**Fix** (`6b7d9fc8`, all in `src/Fleet/Ui`). Each helper now does nothing when it is given what it already shows.
- `FleetRows.Fill` keeps the current source when `FleetRowSource.Shows` the same rows, gaps and headers. It
  still applies the selection.
- `FleetTabBar.Retitle` returns early for the same title.
- `FleetActionBar.Show` swaps in the new callbacks but skips the rebuild when the keys, labels and pin are
  unchanged.
- No dashboard feature code changed.
- Tests: `tests/Fleet.Tests/Ui/UnchangedRedrawTests.cs`. They failed before the fix: the list, the tab bar and
  the action bar were marked dirty or for layout after an unchanged bind.

**Result.**
- Direct pty, idle: 4098 B/s → **10 B/s**, 24 → 1.9 chunks/s (only `ESC[18t`).
- In fleetd, write syscalls and bytes per process (`/proc/<pid>/io`, 10 s idle, 120×35):

| process | before | after |
|---|---|---|
| `fleet dash`, visible | 59.9 writes/s, 1775 B/s | **2.3 writes/s, 15 B/s** |
| `fleet dash`, hidden tab (×2) | 40.6–59.9 writes/s, 1.9–2.9 KB/s | **2.2–2.3 writes/s, 14–15 B/s** |
| `fleet menu` (warm, idle) | 2.0 writes/s | 2.0 writes/s |
| 9 idle dashboards, total CPU (20 s) | 8.85 % | 6.65 % |
| fleetd CPU with those 9 dashboards | 0.70 % | 0.60 % |

The remaining ≈ 2 writes/s per app is TG's `ESC[18t` size poll. The remaining dashboard CPU is the 100
iterations/s main loop (U4). Data that really changes still redraws: `Filling_changed_rows_redraws_the_list`.

## 3. Per-key cost in the menu and the dashboard

Bytes and write syscalls per key for the real pane process under fleetd (`/proc/<pid>/io`, 12 keys, median):

| key | 120×35 before | 120×35 after | 280×75 after |
|---|---|---|---|
| menu Down/Up (float) | 4971 B, 566 writes | 4973 B, 566 writes | 4974 B, 567 writes |
| dashboard `l`/`h` | 2945 B, 98 writes (max 8834 / 292 when a refresh landed) | 2945 B, 98 writes (max 2968 / 100) | 11553 B, 218 writes |

**Menu (AC-4).** A Down/Up runs no fleet code that touches fleetd or the window.
- `NavigationKeyCostTests` drives a float menu through `app.Keyboard`. It asserts zero `FloatScreens.Fit`,
  `FloatScreens.Hold` and float-border publishes, which are the synchronous daemon requests in the UI process.
- It also asserts that only the list, not the window, needs drawing.
- A mutation that adds either to `ValueChanged` makes both tests fail.

**Why one menu key is 567 syscalls anyway.** This is Terminal.Gui 2.4.17, read from the decompiled source:
1. `View.Draw` redraws a view's adornments whenever any subview needs drawing.
2. `OutputBufferImpl.AddGrapheme` marks every written cell dirty, without comparing it with what was there.
3. `OutputBase.Write` walks each dirty row and calls `SetCursorPositionImpl` for **every clean cell**.
4. `AnsiOutput.SetCursorPositionImpl` compares against the logical cursor, not against the last position it
   wrote, so it emits an `ESC[r;cH` each time.
5. Each emit is its own `write(2)` (`UnixIOHelper.TryWriteStdout`).

Outside fleetd, where the menu keeps its border, one Down rewrites all 35 rows: 29.9 KB, and 171 KB at 280×75.
Fleet cannot fix this from its side. `ApplicationImpl(IComponentFactory)` is internal and `Init` only takes a
driver name, so there is no supported way to supply an output that coalesces cursor moves. See the open
questions.

**Dashboard tab switch (AC-7 A/B, not changed).** A tab switch toggles `Visible` on four lists, and TG's
`Visible` setter calls `SuperView.SetNeedsDraw()`. `ShowTab` also calls `window.SetNeedsDraw()`.
- Experiment: a throwaway build gated by `FLEET_AB_TABS`. One always-visible list swaps sources, with no
  `Visible` toggles and no window redraw.
- Result: per-key output dropped only 2945 → 2796 B (98 → 97 writes) at 120×35, and 11553 → 11403 B at 280×75.
- The full-height output comes from the adornment redraw and cursor moves above, not from the tab code. A
  dashboard refactor would not pay off, so none was made.

## 4. Key → frame latency (harness, 40 presses per screen, ms)

e2e is key written → first byte of the redraw. The app hop is shown for the record only (see §1).

| condition | screen | before p50 | before p95 | after p50 | after p95 |
|---|---|---|---|---|---|
| idle 120×35 | menu | 21.5 | 29.8 | 20.4 | 30.0 |
| | picker | 20.9 | 29.0 | 21.8 | 29.5 |
| | dashboard | 21.2 | 31.8 | 22.3 | 32.3 |
| 280×75 + 6 busy + 8 dashboards | menu | 21.4 | 32.3 | 18.9 | 30.1 |
| | picker | 21.2 | 29.6 | 22.0 | 30.3 |
| | dashboard | 34.5 | 46.3 | 34.5 | 44.5 |

- Dashboard tab-switch max: e2e 32.3 → 34.9 ms idle, and 49.4 → 49.0 ms loaded. The app-hop "max" of
  ≈ 4040 ms is the opener artifact in both builds.
- **Key latency did not change, and U2 does not claim it did.** Against the AC-1 baseline (U1, menu e2e p95
  55.5 ms idle and 52.8 ms loaded), the menu is now 30.0 / 30.1 ms. That gain is U4's iteration rate. U2 found
  no fleet-side per-key work left to remove.
- **Menu open (AC-6):** warm first frame 4.0 ms idle before and after, and 6.0 → 5.0 ms loaded.

## 5. Picker reveal hold (≈ 450 ms once per open)

Not changed. The hold is fleetd's reveal gating (`p5 waits to open` → `revealed after drawing`) in
`Platform/Mux/Embedded/Daemon`. That is outside this unit's files and not a trivial change. It costs one key
per picker open, not one per key.

## Open questions

- **TG output.** The biggest remaining per-key cost on Linux is 500+ `write(2)` per menu key, and thousands for
  a full-screen bordered redraw. Either ask Terminal.Gui upstream to skip cursor moves to the position it is
  already at and to batch a frame into one write, or carry a small patch. Windows (`WindowsOutput`) has the same
  `Write` loop, so it would gain too.
- **TG input poll.** TG's 20 ms input poll (`InputImpl` `Task.Delay(20)`) is now the largest share of key → frame
  (app hop p50 ≈ 15 ms). It is U4's area.
- **Harness attribution.** Fix it as in §1, so that dashboard and `--busy` app-hop maxima mean something.
- **Windows.** Not measured. The fix is platform-neutral: fewer `SetNeedsDraw`/`SetNeedsLayout` calls on
  unchanged data.

## How to rerun

```bash
dotnet publish src/Fleet/Fleet.csproj -c Release -r linux-x64 -o out/linux-x64
K() { python3 scripts/e2e/keylatency.py run --presses 40 "$@"; }
K out/linux-x64/fleet
K out/linux-x64/fleet --cols 280 --rows 75 --busy 6 --dashes 8
```

The idle-write, per-key, CPU and stall-probe checks were throwaway scripts on top of `keylatency.Fleet` and
`keylatency.Terminal`:
- idle writes and bytes per key: sample `/proc/<pid>/io` `syscw`/`wchar` around each key, or over 10 s idle;
- frames: timestamp every pty chunk with `keylatency.is_heartbeat = lambda d: False`;
- CPU: sample `utime+stime` from `/proc/<pid>/stat` over 20 s;
- stalls: `app.Iteration` timestamps plus a watchdog thread that runs `gdb -p` (needs `prctl(PR_SET_PTRACER)`
  under `ptrace_scope=1`).
