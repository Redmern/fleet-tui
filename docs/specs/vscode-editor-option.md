# Spec — VS Code editor option

Status: DRAFT, awaiting user approval (Specify gate).

## Problem
Fleet assumes nvim: the "open editor" action, the settings-file editor, the default agent harness (claude hosted in nvim), setup, installer and doctor. Users who do not want nvim need to use VS Code instead.

## Decisions (from interview)
- Per-project setting `editor`: `nvim` | `vscode` | unset.
- Installer writes a global default editor; project setting overrides it.
- VS Code launched as `code -n <worktree>`, detached (no multiplexer pane). Settings file uses `code --wait <file>`.
- Missing `code` on PATH: error toast + doctor warning, no fallback to nvim.
- VS Code projects run the bare-claude harness (no nvim-hosted claude).
- Installer prompts only when neither nvim nor VS Code is found.

## Resolution order (editor for a project)
1. project setting; 2. global default (written by installer); 3. detection: nvim if installed, else VS Code if installed; 4. nvim.

## Scope
- Editor setting (project + global default), menu toggle, resolution.
- "Open editor" action and "edit settings file" honour the resolved editor.
- Harness for new/restored agents and orchestrators follows the resolved editor (vscode → bare claude).
- Setup, doctor, install.sh, install.ps1/deps.ps1: detect `code`, prompt, hints.
- Tests, docs (README, docs/DESIGN.md), RELEASE_NOTES entry if releasing.

## Non-scope
- VS Code extensions/keybinding/theme distribution; claude-code integration inside VS Code.
- Automatic installation in non-interactive mode (prints commands instead).
- Removing or changing nvim behaviour for nvim projects.
- Controlling the VS Code window after launch (focus, close, stop/quit tracking).

## Acceptance criteria
- AC-1: When a project has an editor setting, the system shall use it; otherwise the global default; otherwise the installed editor (nvim preferred); otherwise nvim.
- AC-2: When the user opens the fleet config menu, the system shall show an editor toggle (nvim / vscode) for the current project and persist the choice per project.
- AC-3: When the resolved editor is vscode and the user triggers "open editor" on an agent, the system shall run `code -n <agent worktree>` detached and shall not create a multiplexer pane.
- AC-4: When the resolved editor is vscode and the user edits the settings file, the system shall run `code --wait <file>` and reload settings after it exits.
- AC-5: When the resolved editor is vscode and `code` is not on PATH, the system shall show an error toast with an install hint and shall not fall back to nvim.
- AC-6: When the resolved editor is vscode, the system shall start new, restored and orchestrator agents with the bare claude harness, not nvim-hosted claude.
- AC-7: When the resolved editor is vscode, the system shall not require nvim (no nvim config write, plugin install, or nvim setup error).
- AC-8: When the editor is vscode, agent stop, quit and pane-matching logic shall not depend on an editor pane existing.
- AC-9: When fleet doctor runs and the resolved editor is vscode and `code` is missing, it shall print a warning.
- AC-10: When the installer runs and neither nvim nor VS Code is found, it shall (interactive) prompt nvim / VS Code / skip, install the choice, and record it as the global default; non-interactive it shall print both install commands.
- AC-11: When the installer finds nvim or VS Code, it shall not prompt.
- AC-12: When no editor setting exists anywhere, nvim-only users shall see unchanged behaviour (regression guard).
- AC-13: build, tests, `dotnet format --verify-no-changes` pass; architecture tests (slice boundaries, no comments, AOT JSON context) pass.

## Assumptions
- A1: Existing "main orchestrator / sub orchestrators in nvim" settings stay; a vscode editor overrides them to bare claude.
- A2: Global default stored in a small config file like `nvim.json`.
- A3: Installer is `install.sh` and `install.ps1`/`scripts/deps.ps1`; interactive detection = console attached.
- A4: Changing editor on a running agent affects only the next launch.

## Open questions (deferred, resolve in Plan)
- Q1: Should an existing running nvim-hosted agent be converted when the project editor flips? (Deferred: no; A4.)
- Q2: Linux `code` binary variants (`code`, `codium`, `code-insiders`) — support only `code`? (Deferred: only `code`.)
- Q3: Does the installer's VS Code install on Linux use the package manager or only print a hint? (Decide in Plan; Linux packaging varies.)
