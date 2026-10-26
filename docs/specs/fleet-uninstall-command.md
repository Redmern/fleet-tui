# Spec: fleet uninstall

## Problem
Fleet has no self-uninstall. `install.sh --uninstall` / `install.ps1 -Uninstall` only remove the binary and PATH entry, do not prompt, and know nothing about dependencies (git, nvim, yazi) the installers added. Nothing records which dependencies fleet's installers installed, so removing only those is impossible today.

## Scope
- New verb `fleet uninstall` (Linux, macOS, Windows).
- Installers record dependencies they actually install in a manifest.
- `install.sh --uninstall` / `install.ps1 -Uninstall` delegate to `fleet uninstall` when the binary exists.
- Help text, tests, version bump, RELEASE_NOTES.md entry.

## Non-scope
- Deleting worktrees, repositories, per-project `.mcp.json`, `.claude/settings.local.json` hooks, `.claude/skills/fl-*`, `.fleet/`, `info/exclude` blocks.
- Editing `~/.claude/settings.json`, `~/.claude.json`, Claude keybindings/themes, omarchy `theme-set` hook, yazi config.
- Remote machines reached over ssh.
- Removing dependencies not in the manifest (installs predating the manifest).
- Recursive dependency removal.
- User's own `~/.config/nvim` (even if cloned by installer via FLEET_NVIM_CONFIG).

## Acceptance criteria
- AC-1. When the user runs `fleet uninstall`, the system shall print a plan listing every item it will remove and then ask a single y/N confirmation before changing anything.
- AC-2. When the user answers anything other than yes, the system shall remove nothing and exit 0.
- AC-3. When the user confirms, the system shall stop the fleet daemon, remove the fleet-nvim config, data, state and cache directories, remove the fleet config home (`FleetHome.Config`), remove the shell-rc PATH lines marked `# added by fleet installer` (and the fish `fleet.fish` file if empty) on Linux/macOS, and finally remove the fleet binary.
- AC-4. When the platform is Windows, the system shall also remove the user PATH entry, the taskbar shortcut and the install directory, renaming the running exe aside when it cannot be deleted.
- AC-5. When `--yes` (or `-y`) is passed, the system shall skip the main confirmation and shall not remove dependencies.
- AC-6. When `--yes --remove-deps` is passed, the system shall remove every dependency listed in the manifest without prompting.
- AC-7. When the manifest lists dependencies and the user confirmed the main prompt without `--yes`, the system shall ask a separate y/N per listed dependency and remove only those answered yes.
- AC-8. When removing a dependency, the system shall use the package manager and exact package name recorded in the manifest, non-recursively (brew uninstall, pacman -R, dnf remove, apt-get remove, winget uninstall), using sudo where the install did.
- AC-9. If a dependency removal fails, the system shall report the failure and continue with the remaining steps.
- AC-10. If the manifest is absent, empty or unreadable, the system shall not offer or remove any dependency.
- AC-11. When any of git, nvim or yazi was already present before the installer ran, the installers shall not record it in the manifest.
- AC-12. When `install.sh`, `get-fleet.sh` or `scripts/deps.ps1` installs a dependency successfully, it shall append an entry (tool, package manager, package, UTC time) to `<config home>/installed-deps.json`; a failed install shall record nothing.
- AC-13. After a successful uninstall, the system shall print a note that worktrees, per-project fleet files and `~/.claude*` entries remain.
- AC-14. When `install.sh --uninstall` or `install.ps1 -Uninstall` runs and the fleet binary exists, it shall run `fleet uninstall` (forwarding `--yes` where given); when the binary is missing, it shall use its existing removal logic.
- AC-15. The system shall list `fleet uninstall` and its flags in `HelpCommand.Text`.
- AC-16. When stdin is not a TTY and `--yes` is absent, the system shall exit 1 with a message instead of reading input.
- AC-17. The change shall pass `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes`, and the shell installer tests, and shall obey the repo's architecture tests (slices, no comments in src, AOT).

## Assumptions
- Manifest path is `<FleetHome.Config>/installed-deps.json`; JSON via a source-generated context.
- The fleet-nvim dirs follow XDG (`~/.config|.local/share|.local/state|.cache/fleet-nvim`).
- Delivery: PR to Forgejo (`git push origin <branch>:refs/for/main`), not GitHub; version bumped with a RELEASE_NOTES.md entry.
- spec.md copied into the repo as `docs/specs/fleet-uninstall-command.md`.

## Open questions
None. Deferred: cleaning Claude wiring and per-project files (possible future `--purge`).
