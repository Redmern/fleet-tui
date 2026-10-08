---
name: fl-pr
description: Write the pull request text with evidence and a Merge Danger section, and a short retro for the Learn stage. Use in the Ai-DLC Deliver stage, before gh pr create, or when asked to write a PR or retro.
---

Write the PR for a reviewer who has not seen the work. Open the PR only if the repo has a remote. Never merge.

## PR body
Write it to a file in the worktree's `.fleet` folder (not committed) and use `gh pr create --body-file`. Do not use heredocs.

```
## Summary
<what changed and why, 2 to 4 sentences>

## Evidence
- Build: <command, result>
- Tests: <command, result, count>
- Format: <command, result>
- Before / after: <behavior, output or screenshot path>

## Merge Danger
- Door: one-way (hard to undo) or two-way (easy to undo)
- Blast radius: <who and what is hit if it is wrong>
- Rollback: <how to undo>
- Not covered: <what is not tested>

## Review
<fl-review result: findings and how they were fixed>
```

Rules:
- Show real command output, not "tests pass".
- If a check failed or was skipped, say so.
- Check that the git identity and remote match the profile before you push.
- End the body with the attribution line from the harness.

## Retro (Learn stage)
Write 5 lines or fewer into `learnings.md` (orchestrator) or the final report (agent):
1. What slowed the work.
2. What the agent had to guess.
3. A missing check, tool or doc.
4. A rule for CLAUDE.md, if any. Only suggest it. Do not edit config files.
5. What worked and should repeat.
