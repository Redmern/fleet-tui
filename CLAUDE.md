# fleet

A TUI that orchestrates Claude Code agents across repositories: its own multiplexer (fleetd), Neovim, one git worktree per
agent, and an MCP server Claude uses to drive it. .NET 10, Terminal.Gui 2.4, published with NativeAOT for
win-x64 and linux-x64. Design and rationale: `docs/DESIGN.md`; user-facing behaviour: `README.md`.

## Commands

```powershell
dotnet build Fleet.slnx
dotnet test Fleet.slnx --nologo -v minimal            # ~5,400 xunit tests, ~10-15 s; CI runs it with --filter "Category!=Perf"
dotnet format Fleet.slnx --verify-no-changes           # must pass; the solution is format-clean
dotnet publish src/Fleet/Fleet.csproj -c Release -r win-x64 -o out/win-x64   # AOT: needs the MSVC build tools
.\install.ps1                                          # build from source and install
fleet doctor                                           # end-to-end smoke test on a real machine
```

"Done" means `dotnet build` and `dotnet test` pass. `TreatWarningsAsErrors` is on, so a warning fails the build.
The SDK is pinned in `global.json` (10.0.113, patch roll-forward) and CI installs from it, so local and CI `dotnet format`
agree: patch versions format differently. To bump it, change `global.json`, then update the local SDKs.
Timing-sensitive tests live in the `Perf` category: CI runs them in a separate step that never fails the build, and
`--filter "Category!=Perf"` is what gates.

Smoke-test fleet binaries with `scripts/sandbox-smoke.sh <fleet binary>` (Linux) or `scripts/sandbox-smoke.ps1`
(Windows/pwsh), never by hand. Fleet panes export the real `FLEET_ENDPOINT`; without a sandbox a smoke test of
`fleet uninstall` or any daemon verb can stop the real daemon. The scripts run the binary under `env -i` with a
throwaway HOME, XDG dirs, `XDG_RUNTIME_DIR` and `FLEET_ENDPOINT`, and redirect `CLAUDE_CONFIG_DIR`, `CODEX_HOME` and
`PI_CODING_AGENT_DIR` too; any hand-made sandbox must do the same.

## Layout (`src/Fleet`)

| Folder | What goes there |
|---|---|
| `Cli/Commands` | Commands: they wire things up, no logic that reaches for `Platform` themselves |
| `Cli/Composition` | The composition root: the only place that knows `Platform` implementations |
| `Features/<Slice>` | Vertical slices (Agents, Dashboard, Mcp, Menu, Projects, Repositories, ...) |
| `Ports` | Interfaces the features depend on |
| `Platform` | Implementations of the ports (git, embedded mux, Claude, MCP, storage, ...) |
| `Shared` | Plain types used everywhere; depends on nothing else in Fleet |
| `Ui` | Shared Terminal.Gui helpers and styling (`FleetAsync`, schemes) |

`tests/Fleet.Tests` mirrors this layout. Tests use small hand-written fakes of the ports, not a mocking library.

## Rules the tests enforce (`tests/Fleet.Tests/Architecture`)

Breaking one fails `dotnet test`, so check these before you write code:

- **No comments in `src`.** No `//`, `/* */` or `///` lines at all. Put the why in names, the commit message or
  `docs/`. Tests may have comments.
- A slice never references another slice; features never reference `Platform`; ports and `Ui` depend only on
  `Shared`; `Shared` depends on nothing in Fleet; only the composition root depends on the CLI.
- `Program.cs` is a composition root only; no logic.
- Features don't style themselves: no `Scheme`, `SchemeManager`, `BorderStyle`, `ShadowStyle` or `MessageBox`
  in `Features/`. Styling lives in `Ui`.
- The MCP path (`Features/Mcp`, `Platform/Mcp`) never uses `Console.`: stdout is the protocol.
- Terminal.Gui views run on the UI thread. After an `await` in a dashboard callback, open modals through
  `FleetAsync.OnUi(app, ...)` (see `UiThreadTests`).

## AOT

`src/Fleet` has the AOT and trim analyzers on (`IsAotCompatible`, `InvariantGlobalization`). Avoid reflection,
`dynamic` and reflection-based JSON: serialize through a source-generated context (`[JsonSerializable]` on a
`JsonSerializerContext`, e.g. `ClaudeJsonContext`, `HookJsonContext`, `FleetJsonContext`) and pass its
`Default.<Type>` type info to `JsonSerializer`; add new types there. The analyzers are only in
`src/Fleet`, so the test project may use reflection (xunit needs it).

## Conventions

- Conventional commits, lower case: `feat:`, `fix:`, `docs:`, `chore:`, `perf:`, `refactor:` (in that order of
  frequency). One change per commit.
- The main repository is `redmer/fleet-tui` on the self-hosted Forgejo (`git.home.redmonet.nl`; `origin` is
  `git@192.168.0.213:redmer/fleet-tui.git`). It push-mirrors every branch and tag to `github.com/Redmern/fleet-tui`,
  so never push to GitHub directly: the mirror force-pushes and deletes refs that exist only there. The `github`
  remote is fetch-only. Open pull requests on Forgejo, without a token through AGit
  (`git push origin <branch>:refs/for/main -o topic=<branch> -o title="..." -o description="..."`) or with
  `tea pr create --base main --head <branch>`.
- CI is two halves. `.forgejo/workflows/ci.yml` is the Linux light half on PRs and main: build, non-`Perf` test and
  `Perf` (non-gating) with `DOTNET_gcServer=0`; no format check, no Windows, no AOT. `.github/workflows/ci.yml` runs on
  every branch push the mirror carries to GitHub (no `pull_request` trigger; a newer push cancels the older run of the
  same branch, except on main), on win-x64 and linux-x64: libghostty, installer output (its failure no longer skips the
  tests but still fails the job), test, `Perf`, format, NativeAOT publish and smoke.
- Releases: bump `<Version>` in `src/Fleet/Fleet.csproj` in its own `chore: bump version to X.Y.Z` commit, then
  push a `vX.Y.Z` tag to Forgejo; the mirror carries it to GitHub, where `release.yml` builds and publishes both
  platforms. The same commit adds a
  `## X.Y.Z (yyyy-mm-dd)` entry with 2-6 plain bullets to the top of `RELEASE_NOTES.md`: it becomes the
  GitHub release text and Settings › What's new, and both `dotnet test` and `release.yml` fail without it.
- A project hook (`.claude/settings.json`) whitespace-formats every `.cs` file Claude edits
  (`dotnet format whitespace --folder`, ~2 s). It needs `pwsh` on `PATH`.

## Stacked PRs and delivery

- Rebase a stacked unit with `git rebase --onto <base> <old-base> HEAD`, never onto main's tip, so it carries only
  its own commits. Before handoff, check that `git log <base>..HEAD` lists nothing else.
- Put push and PR permission in the unit's instruction file. A brief that says "do not push" leaves the agent
  blocked at the push in Deliver, and the user has to push.
- After the second PR merges into a shared stack base, build that base before anything else stacks on it: a PR
  that removes a member and a PR that uses it each pass alone and break together.
- When a stack lands through several PRs, diff the integration tip against main before re-cutting the last unit;
  an integration commit can miss main without any PR showing it.
- Merge on Forgejo only, never on GitHub: the mirror force-pushes Forgejo main over GitHub main, so a GitHub
  merge makes the two mains diverge.
- A stacked PR whose base is the previous feature branch merges into that branch, not into main. Target main, or
  merge the stack top-down into main.
- Before Deliver, run a read-only cross-cutting review (standards and spec) of the final tip. It finds defects
  that per-unit reviews miss.
- Push the branch itself to `origin` (`git push origin <branch>`), not only the AGit `refs/for/main` ref: the mirror
  carries only real branches to GitHub, and GitHub is where the Windows and AOT CI runs. Put
  `CI: https://github.com/Redmern/fleet-tui/actions?query=branch%3A<branch with / as %2F>` (or the run URL) in the PR
  text, and check the run with `gh run list -R Redmern/fleet-tui --branch <branch>`.
