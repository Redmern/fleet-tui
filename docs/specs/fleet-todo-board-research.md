# Spec: fleet todo board

Stage: Specify. Repository: fleet-tui only (fleet-frontend untouched). Discovery: discover.md.

## Problem
Ideas for future work have nowhere to go in fleet. Sending them through dispatch starts work immediately. The user wants to capture ideas as tickets, browse them on a board in the terminal, open one to read or edit it, and launch it as work later. Projects with Jira should see their assigned Jira tickets on the same board without leaving the terminal.

## Scope
- Phase 1 (walking skeleton, user approves before Phase 2): ticket storage, capture (`+` sigil, CLI, MCP), per-project board, open in nvim, launch as dispatch, status follow-up, menu and quick-menu entries.
- Phase 2: Jira sync, all-projects aggregate view.

## Non-scope
- Two-way Jira sync (no writes to Jira).
- LLM-generated titles or estimates.
- A web UI. Editing tickets in a built-in form (editing is in nvim).
- Committing tickets to git. Changes to fleet-frontend.

## Decisions (from interview)
1. Board is a native Terminal.Gui list view (pattern: Features/Menu/EditKeybinds). Detail is a markdown file opened in an nvim pane.
2. One file per ticket: `<projectRoot>/.fleet/todos/<NNNN-slug>.md`, YAML frontmatter plus free-text body. Jira tickets use `<KEY>.md`. Folder is untracked.
3. Capture sigil `+` (per-project configurable, default `+`), zero model turn, same hook mechanism as `,`.
4. Effort is a T-shirt size XS, S, M, L, XL; unset on capture.
5. Launch dispatches the ticket as a sub-orchestrator task and links the slug.
6. Quick menu chord `a t` (project board), `a T` (all projects); fleet menu Main entry "Todo board", key `t`.
7. Jira: direct REST, token in `~/.config/fleet/secrets`, pull on board open and manual refresh, pull-only.

## Ticket format
Frontmatter fields: `id`, `title`, `effort` (XS|S|M|L|XL|empty), `status` (open|in-progress|done|closed), `source` (local|jira), `created`, `jira` (key, jira only), `orchestration` (slug, when launched), `note` (optional). Body: free markdown.

## Acceptance criteria

### Storage
- AC-1: When a ticket is created, the system shall write `<projectRoot>/.fleet/todos/<NNNN-slug>.md` where NNNN is the next unused counter and slug derives from the title.
- AC-2: When two tickets are created with the same title, the system shall give them distinct file names.
- AC-3: When a ticket file has valid frontmatter, the system shall load title, effort, status, source and created date from it; when frontmatter is invalid, the system shall list the file with its file name as title and shall not fail the board.
- AC-4: The system shall not read or write ticket files through reflection-based JSON or YAML (NativeAOT-safe).

### Capture
- AC-5: When the user submits a prompt in a main pane starting with the todo sigil followed by text, the hook shall create a ticket titled from the first line of the text (body = full text), block the prompt with no model turn, and show a note naming the ticket id.
- AC-6: When the prompt is only the sigil, the hook shall not create a ticket and shall let the prompt through.
- AC-7: When the todo sigil equals the dispatch sigil in project settings, the system shall reject the setting.
- AC-8: When `fleet todo "<text>"` runs, the system shall create a ticket as in AC-5.
- AC-9: When an orchestrator calls the MCP tool `todo_add` (title, body?, effort?), the system shall create a ticket and return its id; the tool shall appear in the Claude permission planner allow rules.
- AC-10: When the head orchestrator prompt is `+repo: text`, the system shall file the ticket under project `repo`; otherwise under the active project.
- AC-11: When `+` capture is requested and the hook is not installed for the project, `fleet` config sync shall install it next to the dispatch hook without disturbing the dispatch hook's ownership check.

### Board
- AC-12: When the user selects "Todo board" in the fleet menu, or presses chord `a t` in the quick menu, the system shall open the board for the current project.
- AC-13: The board shall list tickets with id, title, effort, status and source, ordered open first, then in-progress, done, closed; newest first within a status.
- AC-14: When the user presses Enter on a ticket, the system shall open its markdown file in an nvim pane (reusing or splitting like OpenEditor) using the configured nvim config.
- AC-15: When the user presses the effort key on a ticket, the system shall cycle its effort and save it to the file.
- AC-16: When the user presses the close key on a ticket, the system shall set status closed; the file shall not be deleted.
- AC-17: When the board is open and a ticket file changes on disk, the board shall show the change on next refresh.
- AC-18: The board shall support vim motions via `FleetKeys.ApplyMotions` and mouse click to select and open (items clickable).

### Launch
- AC-19: When the user presses the launch key on an open ticket, the system shall call the dispatch handler with the ticket body as the prompt, set status in-progress and write the orchestration slug into the ticket.
- AC-20: When the launched orchestration reports done, the system shall set the ticket status to done.
- AC-21: When the launched orchestration reports failed, the system shall set the ticket status back to open and write a note.
- AC-22: When the user presses launch on a ticket that is not open, the system shall not dispatch and shall say why.

### Jira (Phase 2)
- AC-23: Where a project has Jira settings (base URL, email, JQL; default JQL `assignee = currentUser() AND resolution = EMPTY`) and a token under `~/.config/fleet/secrets`, the system shall pull matching issues when the board opens and when the user presses refresh.
- AC-24: When a Jira issue is pulled for the first time, the system shall create `<KEY>.md` with source jira, title and description from Jira, and effort mapped from story points to a T-shirt size.
- AC-25: When a Jira issue already has a file, the system shall update title and description only and shall keep local effort, status, note and body notes below the Jira-owned section.
- AC-26: When an issue no longer matches the query or is closed in Jira, the system shall set the ticket status closed and shall not delete the file.
- AC-27: When Jira is unreachable or credentials are rejected, the board shall still open with cached tickets and show the error in its status line.
- AC-28: The system shall never write to Jira and shall not log the token.
- AC-29: Where a project has no Jira settings, the system shall make no network calls.
- AC-30: When the user presses chord `a T` or the aggregate entry, the system shall open a board listing tickets from all projects with a project column.

### Quality
- AC-31: Every new type obeys fleet-tui architecture tests (slice rules, no comments, no Platform in Features, source-generated JSON), and build, test and `dotnet format --verify-no-changes` pass.
- AC-32: Keybind defaults, README and HelpCommand document the new chords, verb and sigil, and KeysTests pin the new chords.

## Assumptions
- Only fleet-tui changes; storage lives in the project root `.fleet/`.
- Counter allocation is single-writer in practice; collisions resolved by taking next free file name.
- Orchestration done/failed arrives through the existing ReportStatus path and can be hooked there.
- Phase 1 must rebase after `quickmenu-dash-key-focus` merges (same keybind files).

## Open questions
- Deferred to Phase 2: periodic background Jira sync (Phase 2 is open/manual only).
- Deferred: hard delete of tickets (only status closed in this spec).
- To verify in Plan: how an empty stored trigger maps to the default; exact Jira auth scheme for Cloud (email + API token basic auth assumed).
