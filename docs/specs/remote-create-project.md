# Spec: create projects on a remote

Repository: fleet-tui. Branch + PR, never merged by the orchestrator.

## Problem
The head orchestrator can open, switch to and add repositories to projects on a connected remote, but cannot create one. Interactive "new project" on a remote opens the remote's own form and asks nothing on the origin. Triggering case: head wanted a new fleet-frontend project on the Hostinger remote.

## Scope
- Head MCP tool `create_project` (local or any connected remote).
- New wire op `create-project` between origin and remote fleetd.
- Origin UI: Name + Path dialog for a connected remote, from Remote machines screen and the picker's remote "+ New project...".
- Shared duplicate-name check in `CreateProjectHandler` (also affects local form).
- Own permission rule `create_project`, default Ask.
- Docs (HeadBrief.cs, README, RELEASE_NOTES.md, docs/DESIGN.md) and tests.

## Non-scope
- Allowed-roots / path allow-list setting.
- Removing/deleting projects; renaming.
- Changes to fleet-frontend.
- Merging the PR.

## Acceptance criteria
- AC-1. When `create_project` is called with name and path and no `remote` (or `remote: "local"`), the system shall create the project locally, using the same sanitising and persistence as the local New project form.
- AC-2. When `create_project` is called with a `remote` nickname from `list_remotes` that is connected, the system shall create the project on that machine via the `create-project` wire op, with name and path interpreted on that machine.
- AC-3. When a project with the (sanitised) name already exists on the target machine, the system shall refuse with a duplicate-name error and leave the existing project untouched. This applies to the local New project form too.
- AC-4. When the path does not exist and `create_directory` is not true, the system shall refuse with a path error and create nothing. When `create_directory` is true, the system shall create the directory and the project.
- AC-5. When the path is invalid, unresolvable, or is not a directory, the system shall refuse with a path error and create nothing.
- AC-6. When the `remote` is unknown, ambiguous, or not connected, the system shall return an error naming the problem and shall not create any project.
- AC-7. When the remote runs a fleet without the `create-project` op, the system shall return "the fleet on <name> is too old to create projects; update fleet there" and shall create nothing.
- AC-8. When `open` is true (the default) and creation succeeds, the system shall open the project on the target machine and the result shall state "created and opened". When `open` is false, or opening fails, the result shall state "created, not opened" (with the reason on failure) and the project shall remain.
- AC-9. When `repositories` is given, the system shall add them (same accepted forms as `add_repository`) after opening the project; if any fails, the system shall remove the project it created and return an error naming the failing repository, leaving no half-made project.
- AC-10. When `repositories` is given and `open` is false, the system shall reject the call before creating anything.
- AC-11. When `create_project` is invoked, the system shall check the `create_project` permission rule (default Ask) on the machine where the project is created, and shall refuse on Deny/Forbid without creating anything. The rule appears in the settings table and the README permissions text. Calling the tool shall be auto-allowed in the head's Claude like the other head tools; the Ask prompt is the gate.
- AC-12. When ISO mode is on and `remote` is non-local, the system shall refuse with the standard ISO refusal.
- AC-13. When the user presses `n` on a connected machine in the Remote machines screen, or picks "+ New project..." for a remote in the picker, the system shall show a Name + Path dialog (with a "Create directory?" confirm for a missing path), call the same op, open the project on success, and show any error in the dialog without closing it.
- AC-14. The head's CLAUDE.md tool table (`HeadBrief.cs`), README (head tool table, `remote`-taking tools list, permissions, Remote machines section) and RELEASE_NOTES.md shall describe `create_project`.
- AC-15. Tests shall cover: local create, remote create (two-daemon harness), old-remote case, duplicate name, path errors, not-connected, repositories rollback, and permission refusal. `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` pass; architecture rules (no comments in src, slice boundaries, AOT) hold.

## Assumptions
- `create_directory` is the flag name; `open` the open flag (both optional booleans).
- "Not allowed" path = invalid/unresolvable/non-directory; no allow-list exists.
- Project-less permission checks read global/default settings on the target machine.
- Old-remote detection relies on the "unknown operation" error of a new wire op.
- Version bump left to the user's release process (only a RELEASE_NOTES entry if the repo's tests require it for the PR).

## Open questions
- None blocking. Deferred: an allowed-roots setting; whether the version should be bumped in this PR.
