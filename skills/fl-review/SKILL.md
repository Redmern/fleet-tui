---
name: fl-review
description: Review a diff with two read-only reviewers - one for repo standards, one for the spec. Use before a PR, in the Ai-DLC Review stage, or when asked to review a branch.
---

Review the real diff. Do not review a summary of it.

## Fix the base
- Use the branch base (for example `git merge-base HEAD origin/main`). If you cannot find one, ask for it.
- Read `git diff <base>...HEAD` and `git status`.

## Two reviewers in parallel
Start two read-only subagents (the `reviewer` agent, or a general-purpose subagent told not to edit). They must not edit files.

1. **Standards:** check the diff against the repo CLAUDE.md, .editorconfig, analyzers and nearby code. Look for naming and idiom mismatches, duplicated code, unclear code, drive-by changes, missing tests. Skip what a tool already enforces.
2. **Spec:** check the diff against the acceptance criteria in `spec.md` or the task text. For each criterion, name the test or the code that proves it. List missing criteria and work outside the spec.

Both run `dotnet build` and `dotnet test` when a solution exists. They show the exact failures.

## Report
- Show both results side by side, then a short list of findings, worst first.
- For each finding: file and line, what is wrong, a concrete failure case.
- Say "no findings" if there are none. Do not invent findings.
- Mark a finding "uncertain" if you cannot prove it.

## Rounds
- Reviewing another agent's branch: send the findings with `tell_agent`, ask for fixes, then review again.
- Reviewing your own branch: fix the findings yourself, then review again.
- Stop after 2 rejected rounds and ask the user.
