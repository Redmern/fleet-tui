---
name: fl-discuss
description: Interview the user about a plan before the spec is written - rounds of numbered questions, each with a recommended answer. Use at the Ai-DLC Specify stage for feature and refactor tasks, when the task has open decisions, or when told to "discuss" or "grill".
---

Interview the user until you both agree on what to build. Do not write the spec before the user confirms.

## Map the decisions
Treat the task as a tree. Each decision leads to other decisions. The **frontier** is every decision whose parents are already settled.

## Work in rounds
1. Ask the whole frontier in one round with AskUserQuestion. Ask at most 4 questions in a round.
2. Give each question a recommended answer. Put it first and mark it "(Recommended)". Word it so that picking it accepts the recommendation.
3. Wait for the answers. Then find the new frontier and ask the next round.
4. Do not ask a question whose answer depends on an open question in the same round.

## Facts are your job
- Find facts yourself with a read-only subagent: code, config, docs, repository state.
- Ask the user only for decisions, never for a fact you can look up.
- Do not wait for a subagent if other questions are ready. Ask those first.

## Stop
- Stop when the frontier is empty and nothing is silently assumed.
- Write the result into the spec: decisions as requirements, unresolved items under "open questions".
- Tell the user the summary and ask for confirmation. Do not act before the user confirms.

## Skip it
- Skip the interview for express and bugfix profiles, unless the task is vague.
- If nobody can answer (the user is not present), write the questions and your recommended answers into `spec.md` under "open questions" and stop at the Specify gate.
- Never answer a question for the user.
