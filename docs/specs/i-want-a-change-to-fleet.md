# Spec: launch folder as root for new projects

## Problem
New-project dialog (`CreateProjectView`) opens with an empty Root; the folder browser (yazi) then starts in `$HOME`. The folder `fleet` was started from is ignored. Also, the menu/pick process is spawned by `fleetd`, whose cwd is where the daemon first started, so the true launch folder is lost.

## Scope
- fleet-tui only.
- Carry the client's launch folder to the menu/pick process.
- Use it as default Root and as the browse start folder when creating a project.
- Both creation paths: `PickProjectCommand` and `MenuCommand.NewProjectFrom`.

## Non-scope
- Changing how existing projects are listed/resolved.
- Changing `CreateProjectHandler` validation.
- Remembering last-used folders.

## Acceptance criteria
- AC-1: When the New project dialog opens with no draft and a usable launch folder, the system shall prefill Root with that folder.
- AC-2: When the user opens the folder browser, the system shall start it in the folder typed in Root, else in the launch folder, else in home.
- AC-3: When the attach client connects, the system shall send its current directory to the daemon, and the daemon shall pass it to the menu/pick process it spawns, even when the daemon was started from another folder.
- AC-4: When the launch folder is missing, empty, or does not exist, the system shall behave as today (empty Root, browse starts in home).
- AC-5: When the dialog is reopened from a draft (return from terminal browse), the system shall prefill Root from the draft, not the launch folder.
- AC-6: The system shall apply AC-1..AC-5 to both `PickProjectCommand` and `MenuCommand.NewProjectFrom`.
- AC-7: When a client without a cwd in Hello (older client) connects, the daemon shall accept it and fall back per AC-4.
- AC-8: The user shall still be able to edit Root freely.
- AC-9: `dotnet build`, `dotnet test`, and `dotnet format --verify-no-changes` pass; no comments added to `src`; feature slices do not read `Environment.CurrentDirectory`.

## Decisions (from interview)
- Prefill + browse start; transport via `Hello.Cwd`; fall back to home as today; both paths.

## Assumptions
- Launch folder = `Environment.CurrentDirectory` of the `fleet` attach client.
- Existence checked in Cli layer, slices receive a plain path.
- Hello change is additive/optional (backward compatible; protocol version unchanged unless tests require otherwise).

## Open questions
- None blocking. Deferred: whether `fleet` started from `$HOME` should prefill home (default: yes, trivially same as launch folder).
