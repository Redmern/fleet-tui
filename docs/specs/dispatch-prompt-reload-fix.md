# Spec — dispatch-prompt-reload-fix

## Problem
In the main orchestrator pane, a hand-typed prompt starting with the dispatch trigger (default `,`) is intercepted by the `UserPromptSubmit` hook (`fleet hook-dispatch`) and dispatched to a sub-orchestrator. The hook blocks the prompt (exit code 2, or `{"decision":"block"}` with `FLEET_HOOK_BLOCK=json`). Afterwards the original `, task` text reappears in the orchestrator's input field. The user must clear it by hand, and may resend it by accident.

## Scope
- `fleet hook-dispatch` output/exit behaviour for taken prompts (`HookDispatchCommand`).
- `fleet hook-todo` (`HookTodoCommand`), if it shows the same symptom; shared code so both hooks behave alike.
- Tests for the changed behaviour.

## Non-scope
- Prompts relayed by the head (`HeadRelay`).
- Dispatch routing, sub-orchestrator behaviour, trigger settings, hook install/ownership logic, other hooks.
- Changing what the user sees as the dispatch confirmation note, unless the fix requires moving where it is shown.

## Acceptance criteria
- AC-1: When a typed prompt starting with the trigger is dispatched by the hook, the main pane's input field shall be empty afterwards (the prompt text is not restored).
- AC-2: When the hook dispatches a prompt, the main orchestrator shall not run a model turn for it (unchanged behaviour).
- AC-3: When the hook dispatches a prompt, the user shall still see the dispatch note (or the failure note, `fleet: could not dispatch — ...`).
- AC-4: When a prompt does not start with the trigger, is empty after the trigger, or is a Claude peer message, the hook shall pass it through unchanged (exit 0, no output).
- AC-5: When `hook-todo` takes a prompt, the prompt shall not be restored to the input field either, if the symptom reproduces for it; both hooks shall share the same block mechanism.
- AC-6: If no hook output form stops Claude Code restoring the text, the system shall clear the input field after the block as a fallback, and the choice shall be documented in `design.md`.
- AC-7: `dotnet build`, `dotnet test` and `dotnet format --verify-no-changes` shall pass; no comments in `src`.

## Assumptions
- Cause is Claude Code restoring a blocked prompt into the input (Claude Code 2.1.310). Unverified; the Build unit verifies it first.
- `FLEET_HOOK_BLOCK=json` vs exit-2 may behave differently; only the form fleet installs by default matters.

## Open questions
- Which hook output form (if any) stops the restore without a model turn: deferred to the Build unit's investigation, reported in `progress/`.
- Whether `hook-todo` shows the same symptom: deferred, same investigation.
