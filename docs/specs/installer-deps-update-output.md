# Spec: installer-deps-update-output

## Problem
1. Installing fleet on a machine without git, nvim or yazi: installers only check/install them under opt-in `--with-deps` (and `install.sh` skips the check entirely otherwise), so the user finds out late via `fleet setup`.
2. `fleet update --list` prints every release (up to 100), one per line. Too long to read.

## Scope
- Repo: fleet-tui only.
- Installers: `install.sh`, `scripts/get-fleet.sh`, `install.ps1` (+ `scripts/deps.ps1`), `scripts/get-fleet.ps1`.
- `fleet update --list` output (`UpdateCommand.ListAsync`, new formatter, `--all` flag in `CommandLine`, `Invocation`, help text).
- Tests, `RELEASE_NOTES.md` entry and version bump per repo rules.

## Non-scope
- dotnet, clang, claude prerequisites.
- Release dates, change to `IReleaseClient` / `ReleaseInfo` / GitHub JSON parse.
- Plain `fleet update` (install flow) output, the TUI version screen.
- `fleet doctor` / `fleet setup` behaviour.
- Windows non-winget package managers.

## Acceptance criteria

### Installer deps
- AC-1: When an installer runs (any flags), the installer shall check whether git, nvim and yazi are on PATH and print which are missing.
- AC-2: When all three are present, the installer shall print no prompt and install nothing.
- AC-3: When any are missing and stdin/stdout is an interactive terminal and neither `--with-deps` / `-WithDeps` nor a skip env is set, the installer shall list the missing tools and ask y/N before installing them with the detected package manager.
- AC-4: When the user answers no (or presses Enter), the installer shall install nothing, print the exact install command(s), and continue the install.
- AC-5: When any are missing and the run is non-interactive (e.g. `curl | sh`, CI) without `--with-deps`, the installer shall install nothing and print the exact install command(s) for the detected package manager.
- AC-6: When `--with-deps` / `-WithDeps` is given, the installer shall install the missing tools without asking (existing behaviour preserved).
- AC-7: Where the OS is macOS and `brew` is present, `install.sh` shall install via `brew install git neovim yazi` (missing ones only). pacman, dnf and apt behaviour is kept.
- AC-8: When the package manager is apt, `install.sh` shall install git and nvim and, for yazi, print the official install hint and continue.
- AC-9: If no known package manager is found (or winget is missing on Windows), the installer shall print the missing tools with manual install guidance and continue.
- AC-10: If a dependency install fails, the installer shall warn and continue (non-fatal); exit status reflects the fleet install, not the optional deps.
- AC-11: `scripts/get-fleet.sh` and `scripts/get-fleet.ps1` shall apply AC-1..AC-6, AC-9, AC-10 and, for `get-fleet.sh`, AC-7/AC-8 (interactive prompt reads from the terminal, not the piped script).
- AC-12: The Windows installers shall detect via PATH and install missing tools through the existing winget IDs (Neovim.Neovim, Git.Git, sxyazi.yazi), skipping present ones.

### Update list
- AC-13: When `fleet update --list` (or `-l`) runs, the system shall print a header with the installed version and the latest stable version.
- AC-14: When listing, the system shall show the newest 10 non-draft, non-prerelease releases as an aligned table with markers for installed and latest.
- AC-15: When more releases exist than shown, the system shall print a footer "... N older, use --all" with the exact count N.
- AC-16: When `--all` is given with `--list`, the system shall show every release including prereleases, with prerelease markers and no cap and no footer.
- AC-17: If the installed version is not within the shown releases, the system shall still show it in the header.
- AC-18: If no releases are found, the system shall keep the current "none found" message and exit code 1.
- AC-19: `--all` shall be parsed in `CommandLine`, documented in help text, and covered by `CommandLineTests`.
- AC-20: The list formatting shall live in a testable component with unit tests (no direct Console in the formatter); `src` has no comments and passes architecture tests.

### Delivery
- AC-21: `dotnet build`, `dotnet test`, `dotnet format --verify-no-changes` pass; `RELEASE_NOTES.md` has an entry with a matching `<Version>` bump.

## Assumptions
- Work happens on a branch off the current release worktree; base branch chosen at Plan.
- Interactive = TTY on stdin (or /dev/tty reachable) and not CI.
- Skip env: existing `CI` / a new `FLEET_NO_DEPS_PROMPT` treated as non-interactive. (Name finalised in design.)
- "Latest" = newest non-prerelease release returned by the API.

## Open questions
- None blocking. Deferred: exact table column layout and the skip env var name (design.md).
