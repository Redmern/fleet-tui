# fleet

A TUI that orchestrates Claude Code agents across repositories: its own multiplexer (fleetd), Neovim, one git worktree per
agent, and an MCP server Claude uses to drive it. .NET 10, Terminal.Gui 2.4, published with NativeAOT for
win-x64 and linux-x64. Design and rationale: `docs/DESIGN.md`; user-facing behaviour: `README.md`.

## Commands

```powershell
dotnet build Fleet.slnx
dotnet test Fleet.slnx --nologo -v minimal            # ~925 xunit tests, ~40 s; this is what CI runs
dotnet format Fleet.slnx --verify-no-changes           # must pass; the solution is format-clean
dotnet publish src/Fleet/Fleet.csproj -c Release -r win-x64 -o out/win-x64   # AOT: needs the MSVC build tools
.\install.ps1                                          # build from source and install
fleet doctor                                           # end-to-end smoke test on a real machine
```

"Done" means `dotnet build` and `dotnet test` pass. `TreatWarningsAsErrors` is on, so a warning fails the build.

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
- Releases: bump `<Version>` in `src/Fleet/Fleet.csproj` in its own `chore: bump version to X.Y.Z` commit, then
  push a `vX.Y.Z` tag; `release.yml` builds and publishes both platforms.
- A project hook (`.claude/settings.json`) whitespace-formats every `.cs` file Claude edits
  (`dotnet format whitespace --folder`, ~2 s). It needs `pwsh` on `PATH`.
