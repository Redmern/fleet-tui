# Spec: menu icons and keybinds

## Problem
The New agent and Add repository dialogs show plain text pill buttons and field rows with no icons, and button keys are not visible or mnemonic. New sub-orchestrator has no dedicated dialog (picker + prompt only). Pill helper `Choice()` is duplicated in both views.

## Scope
- New agent, Add repository, New sub-orchestrator dialogs.
- Icons on buttons, field rows, dialog titles.
- Visible, working keybinds on buttons.
- Shared pill-button component in `Ui/`.
- Field hotkeys; remembered defaults in New agent.

## Non-scope
- Other dialogs (themes, settings, confirm dialogs).
- New icon fallback logic beyond the existing ButtonHints setting.
- Changing handlers' business logic (NewAgentHandler, AddRepositoryHandler, DispatchHandler).

## Acceptance criteria
- AC-1: When the New agent, Add repository or New sub-orchestrator dialog opens, the system shall show a Nerd Font icon in its title.
- AC-2: When any of the three dialogs renders its action buttons (Create agent / Add repository / Dispatch, and Cancel), the system shall show an icon and a key chip in each button pill.
- AC-3: When a field row (Repo, Branch, Base, Opens, Clone, Name, URL, Task) is rendered, the system shall show a leading icon.
- AC-4: While a list row has focus, when the user presses a button's mnemonic (`c` create agent, `a` add repository, `d` dispatch), the system shall activate that button.
- AC-5: When the user presses Enter on a button row, the system shall activate it; when the user presses Esc, the system shall cancel the dialog (unchanged behaviour).
- AC-6: While a text prompt (name, URL, task) has focus, when the user types a letter, the system shall insert it as text and not trigger a mnemonic.
- AC-7: While a text prompt has focus, when the user presses Ctrl+Enter, the system shall submit the dialog.
- AC-8: When the user starts New sub-orchestrator, the system shall open a dedicated dialog with a task field and Dispatch/Cancel pill buttons, offering past tasks, replacing the picker + prompt flow, and call DispatchHandler as before.
- AC-9: The system shall render pill buttons for all three dialogs from one shared component in `Ui/`; the private `Choice()` helpers in NewAgentView and AddRepositoryView shall be removed.
- AC-10: While a list row has focus in New agent, when the user presses `r`, `b` or `o`, the system shall move to the Repo, Branch or Opens row and open its picker.
- AC-11: When New agent opens, the system shall preselect the last-used repository and harness if still valid; when an agent is created, the system shall store them.
- AC-12: While ButtonHints is Text, the new pills shall show text names; None or Tooltips follow existing FleetButtonHints behaviour.
- AC-13: Backspace-back handling in these views shall keep working (MenuBackTests pass), and mnemonic keys shall not collide with existing dialog keys.
- AC-14: `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` shall pass; no comments in src; styling only in `Ui/`.

## Assumptions
- Icons are Nerd Font glyphs, added as constants in Ui (no NewAgent/NewSub/AddRepo constant exists yet).
- Remembered defaults live in the existing settings store.
- Field hotkeys `r`/`b`/`o` apply to New agent only (Add repository fields: out of scope for jump keys).
- The new dialog follows the same FleetTheme.Overlay + FleetList pattern as NewAgentView.

## Open questions (deferred)
- Exact glyph choices: decided at build, shown in the walking-skeleton review.
- Which settings file/section stores remembered defaults: decided in Plan.
