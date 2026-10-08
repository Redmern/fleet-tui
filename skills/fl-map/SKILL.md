---
name: fl-map
description: Chart a map of decision tickets for work too big for one session, then resolve one ticket per session until the way is clear. Use when an effort has fog (many unknowns), or when the instructions say "map:". Not for work one session can hold.
---

This skill plans. It does not build. The end result is a decision or a spec, which then goes into a normal Ai-DLC profile.

## Is a map needed?
Look at the task. If the way to the goal is clear and one session can hold it, stop. Tell the user and use plain Ai-DLC.

## Files (in the orchestration folder)
- `map.md`: the index.
- `tickets\NN-short-name.md`: one question each.

`map.md` sections:
```
## Destination      <what the end looks like: a spec, a decision, a change>
## Notes            <domain, skills to use, preferences>
## Decisions so far - [ticket name](tickets\NN-name.md): <one-line answer>
## Not yet specified   <known unknowns you cannot state sharply yet>
## Out of scope     <ruled out, with the reason>
```

Ticket file:
```
type: research | prototype | discuss | task
blocked-by: <ticket names, or none>
claimed-by: <session or none>
status: open | closed

## Question
<one decision, small enough for one session>

## Answer
<written when closed>
```

Refer to tickets by name, not by number alone. Only the sub-orchestrator writes `map.md`.

## Chart the map (one session)
1. Name the destination. Use `fl-discuss`.
2. Map the frontier: ask broad questions to find the open decisions and the unknowns.
3. Write `map.md`. Put sharp questions in tickets. Put unclear ones in "Not yet specified".
4. Create tickets first, then fill `blocked-by` in a second pass.
5. Start one read-only subagent for each `research` ticket. Write its findings to `reports\`.
6. Stop. Charting resolves no ticket.

## Work the map (one ticket per session)
1. Read `map.md`. Pick the first open ticket that is unblocked and unclaimed. Set `claimed-by` before any work.
2. Resolve by type:
   - research: subagent reads sources, writes to `reports\`.
   - discuss: use `fl-discuss` with the user. Never answer for the user.
   - prototype: make a cheap rough artifact for the user to react to.
   - task: do the manual step, or give the user an exact checklist.
3. Write the answer in the ticket, set `status: closed`, add one line to "Decisions so far".
4. Move newly clear items from "Not yet specified" into tickets. Move out-of-scope items to "Out of scope". Change or remove tickets the answer made wrong.

## Finish
When no ticket is open, the destination is clear. Write `spec.md` and start the Ai-DLC profile that fits (usually feature or refactor).
