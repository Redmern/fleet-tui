---
name: fl-tdd
description: Test-first build loop for .NET repo agents, plus a structured bug-fix flow. Use when building a unit in the Ai-DLC Build stage, fixing a bug, or when told to use TDD.
---

Write the test first. Then write the smallest code that passes it. Then clean up.

## Choose the seams first
- A seam is the place where a test checks behavior: a public method, a handler, an HTTP endpoint.
- Read the spec. Test through the public interface at the seams named in the spec. If none are named, choose them and write them in your first commit message.
- Do not test private details. A good test still passes after a refactor.

## Loop (one behavior at a time)
1. **Red:** write one test for one behavior. Run it: `dotnet test --filter <name>`. Make sure it fails for the right reason.
2. **Green:** write the smallest code that passes. Run the same test.
3. **Refactor:** clean up with all tests green. Do not change behavior.
4. Commit small, with a conventional commit message.

Do not write all tests first and all code after. Work in thin vertical slices.

## Bad tests
- The test repeats the implementation.
- The test passes without the change. Revert the change and check.
- The test checks a mock call and not an outcome.

## Fixing a bug
1. Build a fast way to see the bug. A failing test is best.
2. Make the failure small: remove everything not needed to show it.
3. List 3 to 5 causes, most likely first. Test the first one with a log or a probe. Remove probes after.
4. Fix the cause, not the symptom.
5. Keep the failing test as the regression test.
6. If you cannot reproduce the bug, say so. Do not guess a fix.

## Done
Use the `dotnet-verify` skill: `dotnet build` (warnings count), `dotnet test`, `dotnet format --verify-no-changes`. Show the failures. Do not report done while one fails.
