# Spec: theme tabs, more themes, custom colors

## Problem
Theme picker is a flat list of 15 themes (12 dark, 3 light) with no way to set own colors.

## Scope
- Theme picker gets three tabs: Dark | Light | Custom.
- Built-in themes grow from 15 to 33 (+18), weighted to light.
- Custom tab: create/edit custom themes from a base theme, edit any color role.
- Update README Themes section, tests, help text if CLI changes.

## Non-scope
- No live whole-UI preview while editing.
- No palette-grid picker.
- No new `fleet theme` verbs (custom themes already appear in `list`/`set`).
- No per-project themes.

## Acceptance criteria
- AC-1: When the user opens the theme action (`T`), the system shall show a tabbed picker with tabs Dark, Light, Custom, opening on the tab holding the active theme with it preselected and marked "(active)".
- AC-2: When the user switches tabs (Left/Right or PrevTab/NextTab), the system shall list only that tab's themes.
- AC-3: The system shall provide exactly 33 built-in themes, each with all 14 roles + 8 ansi + 8 brights, unique names equal to `ThemeNames.Normalize(Title)`, with catppuccin-mocha remaining default and first.
- AC-4: The Dark tab shall list built-ins with `Light=false` and the Light tab those with `Light=true`; no built-in appears in both; at least 15 built-ins shall be light (3 existing + at least 12 new).
- AC-5: When the user accepts a theme on Dark/Light/Custom, the system shall call `themes.Set` and `ThemeWiring.Apply` as today and show `FleetDialog.Error` on failure.
- AC-6: The Custom tab shall list existing custom themes (including omarchy-synced) and a "New custom theme…" entry.
- AC-7: When the user chooses "New custom theme…", the system shall ask for a base theme (any built-in or custom), then a name; the name shall be normalized and rejected if empty or equal to a built-in name.
- AC-8: When editing a custom theme, the system shall list all 30 colors (14 roles, 8 ansi, 8 brights) grouped, each with swatch and hex value.
- AC-9: When the user selects a color, the system shall open a dialog with R/G/B sliders (arrow keys: ±1; larger step for ±16), a hex field validated by `HexColor.Normalize` (invalid input rejected, previous value kept), and a live swatch.
- AC-10: When the user saves, the system shall write `<config>/themes/<name>.toml` with `inherits = "<base>"` and only the changed colors, and shall set `light` from `HexColor.IsLight` of the resulting base color.
- AC-11: When a saved custom theme is loaded, the system shall resolve unchanged colors from the base theme (via `CustomTheme.FromToml`).
- AC-12: When the user cancels the color dialog or editor, the system shall not modify any file.
- AC-13: When a custom theme is saved while it is the active theme, the system shall refresh running windows via the existing ThemeWatcher path.
- AC-14: When the user deletes a custom theme (key on Custom tab, with confirm), the system shall remove its file; if active, fall back to catppuccin-mocha.
- AC-15: Where a custom theme is Light, the Custom tab shall still list it (Custom tab is not split by light/dark).
- AC-16: README Themes section shall list the new themes and describe the tabs and custom editor; architecture tests, format check and full test suite shall pass.

## Assumptions
- Real code lives at /home/red/repos/fleet/fleet-tui/main; work happens on a branch there.
- Reuse FleetTabbedPicker, FleetPrompt, FleetModal, FleetTheme widgets; new view obeys architecture rules (styling in Ui, no comments in src, no cross-slice refs).
- Editing an existing custom theme overwrites it. Omarchy theme edit saves under the same custom name.
- 33 = +18 new themes; palettes authored from well-known public schemes.
- Custom-theme delete (AC-14) is a small addition for manageability; drop if user objects.

## Decisions (from interview)
Tabbed picker reuse; +18 themes mostly light; sliders + hex dialog; all 30 colors; user-chosen name, auto light flag; swatch-only preview, apply on save.

## Open questions
- Deliver: user chose **Forgejo** instead of GitHub. Remote URL, CLI and auth are **deferred** to the Deliver gate; I will ask then.
- AC-3/AC-4 exact dark/light counts: deferred to Plan (constraint: total 33, ≥12 new light).
