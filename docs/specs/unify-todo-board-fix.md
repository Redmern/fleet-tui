# Spec — unify-todo-board-fix

## Problem
fleet-tui 0.7.16 has two todo board menu items: "Todo board" (current project, `t`, chord `a t`) and "All todos" (all projects, `T`, chord `a T`). The user wants one item with an in-board switch between the two scopes. Also, opening a board shows an error, so the boards do not work at all. The cause is not yet known (no error in `~/.config/fleet/fleet.log`).

## Scope
- fleet-tui only. Branch from `origin/main`.
- Reproduce and fix the error on opening the board.
- One menu item, one chord, one board with a scope toggle.
- Update tests, README, RELEASE_NOTES, and `fleet help` text for the change.

## Non-scope
- Jira sync, launching, effort/close behaviour, ticket storage format.
- fleet-frontend.
- Any new persistence of the chosen scope.

## Acceptance criteria
- AC-1: When the user opens the todo board from the fleet menu, the quick menu or the `a t` chord, the system shall show the board without an error, for a project with tickets, a project with no `.fleet/todos` folder, and in all-projects scope with every registered project (including projects whose root is missing or unreadable).
- AC-2: When the cause of the open error has been found, the system shall have a regression test that fails on `origin/main` and passes after the fix, and the cause shall be recorded in progress/ and the PR text.
- AC-3: The fleet main menu shall list one todo board item, labelled "Todo board", bound to `t`; it shall not list "All todos".
- AC-4: The system shall bind the chord `a t` to the todo board and shall not define `a T`, the `T` key, the `todo-board-all` action, or `FleetAction.TodoBoardAll`.
- AC-5: When a user keybind file still references `todo-board-all`, the system shall load without error and ignore that entry.
- AC-6: When the board opens, the system shall show the current project's tickets only (project scope).
- AC-7: When the user presses `a` in the board, the system shall switch between project scope and all-projects scope, refresh the list, and update the window title.
- AC-8: While the board is in all-projects scope, the system shall show each ticket's project name; in project scope it shall not.
- AC-9: When the scope changes and the selected ticket exists in the new scope, the system shall keep it selected; otherwise it shall select the first row.
- AC-10: The board's action bar shall show an `a` hint labelled with the scope the key switches to.
- AC-11: When the scope changes, the system shall keep open, launch, effort, close, refresh and Jira sync working against the right project's ticket in both scopes.
- AC-12: README, RELEASE_NOTES and `fleet help` shall describe the single board and the `a` toggle, with no mention of `T` / "All todos".
- AC-13: `dotnet build Fleet.slnx`, `dotnet test Fleet.slnx` and `dotnet format Fleet.slnx --verify-no-changes` shall pass.

## Assumptions
- The board scope is not remembered between opens (always project first).
- `a` does not clash with an existing board key (board keys: enter, l, e, x, refresh, quit/back).
- Delivery is one unit/PR; the error fix lands first as the walking skeleton.

## Open questions
- Exact error text on open: unknown; the build agent reproduces it. Deferred to Build (AC-2).
