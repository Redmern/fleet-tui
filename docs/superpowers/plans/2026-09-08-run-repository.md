# Run a Repository Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fleet can start, stop and report a repository's application from the Repositories tab and from agent MCP tools, using a per-repository run profile (command, port, path).

**Architecture:** A `RunProfile` per repository lives in a JSON store behind an `IRunStore` port. Three handlers in the new `Features/Repositories/RunRepository` slice spawn, kill and configure; a run pane is identified by its tab title `<repo> run`, never by cwd, and the two existing cwd matchers learn to skip it. Status is derived from live panes only.

**Tech Stack:** C# / .NET 10, NativeAOT (source-generated JSON only), xunit, Terminal.Gui, wezterm via `IMuxDriver` + `FakeMuxDriver`.

**Spec:** `docs/superpowers/specs/2026-09-08-run-repository-design.md`

**House rules (enforced by `tests/Fleet.Tests/Architecture/SliceBoundaryTests.cs`):**
- No `//` or `/* */` comments anywhere under `src/`. Put reasoning in test names and commit messages.
- `Features/*` never references `Fleet.Platform.*`.
- A slice `Features/<Area>/<Slice>/` never references another slice's namespace. Files directly under `Features/<Area>/` (the area root) are shared within the area.
- Every commit goes straight to `main` (`git commit`, no branch). Run the full suite before each commit:

```
cd C:/repos/fleet/tests/Fleet.Tests && dotnet test 2>&1 | grep -E "error CS|\[FAIL\]|Passed!|Failed!"
```

Expected on success: one line `Passed!  - Failed:     0, Passed:   <n>, ...`.

- Test names are `Snake_case_sentences`. Test doubles live inside the test class (see `RecordingStore` in `tests/Fleet.Tests/Features/Agents/OpenAgentTests.cs`).
- The shell here is Git Bash on Windows. Write files with the Write tool; multi-line shell heredocs have bitten before.

---

## Chunk 1: Titles, shell line, store, status, matchers

### Task 1: Run tab title helpers

**Files:**
- Modify: `src/Fleet/Shared/Constants/FleetTabTitles.cs`
- Test: `tests/Fleet.Tests/Shared/FleetTabTitlesTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Shared.Constants;

namespace Fleet.Tests.Shared;

public class FleetTabTitlesTests
{
    [Fact]
    public void A_run_tab_is_named_after_its_repository()
    {
        Assert.Equal("backend run", FleetTabTitles.Run("backend"));
    }

    [Fact]
    public void A_run_title_is_recognised_and_agent_titles_are_not()
    {
        Assert.True(FleetTabTitles.IsRun("backend run"));
        Assert.True(FleetTabTitles.IsRun("Backend RUN"));
        Assert.False(FleetTabTitles.IsRun("backend/feature_run"));
        Assert.False(FleetTabTitles.IsRun("remove-pr-pipeline"));
        Assert.False(FleetTabTitles.IsRun(FleetTabTitles.Dashboard));
        Assert.False(FleetTabTitles.IsRun(string.Empty));
    }
}
```

- [ ] **Step 2: Run it, expect a compile failure**

Run: `cd C:/repos/fleet/tests/Fleet.Tests && dotnet test --filter "FullyQualifiedName~FleetTabTitlesTests" 2>&1 | grep -E "error CS|Passed!|Failed!"`
Expected: two `error CS0117` lines, `'FleetTabTitles' does not contain a definition for 'Run'` and one for `'IsRun'`

- [ ] **Step 3: Implement**

Replace `src/Fleet/Shared/Constants/FleetTabTitles.cs` with:

```csharp
namespace Fleet.Shared.Constants;

public static class FleetTabTitles
{
    public const string Dashboard = "dash";

    private const string RunSuffix = " run";

    public static string Run(string repository) => repository + RunSuffix;

    public static bool IsRun(string title) =>
        title.EndsWith(RunSuffix, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Run the test, expect pass**

Same command. Expected: `Passed!  - Failed:     0, Passed:     2`.

- [ ] **Step 5: Commit**

```
cd C:/repos/fleet && git add src/Fleet/Shared/Constants/FleetTabTitles.cs tests/Fleet.Tests/Shared/FleetTabTitlesTests.cs && git commit -q -m "feat: name run tabs '<repo> run' and recognise them"
```

### Task 2: ShellLine in Shared, adopted by EnvLaunch

**Files:**
- Create: `src/Fleet/Shared/ShellLine.cs`
- Modify: `src/Fleet/Platform/Mux/WezTerm/EnvLaunch.cs`
- Test: `tests/Fleet.Tests/Shared/ShellLineTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Shared;

namespace Fleet.Tests.Shared;

public class ShellLineTests
{
    [Fact]
    public void On_windows_the_line_runs_under_cmd()
    {
        Assert.Equal(["cmd", "/c", "npm run dev"], ShellLine.Command(windows: true, "npm run dev"));
    }

    [Fact]
    public void Elsewhere_the_line_runs_under_sh()
    {
        Assert.Equal(["sh", "-c", "npm run dev"], ShellLine.Command(windows: false, "npm run dev"));
    }

    [Fact]
    public void The_line_is_passed_through_untouched_quotes_included()
    {
        var line = "dotnet run --urls \"http://localhost:5000\"";

        Assert.Equal(line, ShellLine.Command(windows: true, line)[2]);
        Assert.Equal(line, ShellLine.Command(windows: false, line)[2]);
    }
}
```

- [ ] **Step 2: Run it, expect compile failure** (`ShellLine` does not exist).

- [ ] **Step 3: Implement**

`src/Fleet/Shared/ShellLine.cs`:

```csharp
namespace Fleet.Shared;

public static class ShellLine
{
    public static IReadOnlyList<string> Command(bool windows, string line) =>
        windows ? ["cmd", "/c", line] : ["sh", "-c", line];
}
```

Then make `EnvLaunch.Wrap` use it. Replace the body of `src/Fleet/Platform/Mux/WezTerm/EnvLaunch.cs` with:

```csharp
using Fleet.Shared;

namespace Fleet.Platform.Mux.WezTerm;

public static class EnvLaunch
{
    public static IReadOnlyList<string> Wrap(
        bool windows, IReadOnlyDictionary<string, string> env, IReadOnlyList<string> command)
    {
        var run = string.Join(' ', command);

        if (windows)
        {
            var parts = env
                .Select(kv => $"set {kv.Key}={kv.Value}")
                .Append(run);

            return ShellLine.Command(windows: true, string.Join("& ", parts));
        }

        var lines = env
            .Select(kv => kv.Value.Length == 0 ? $"unset {kv.Key}" : $"export {kv.Key}={kv.Value}")
            .Append($"exec {run}");

        return ShellLine.Command(windows: false, string.Join("; ", lines));
    }
}
```

- [ ] **Step 4: Run the full suite** — `EnvLaunchTests` must still pass unchanged. Expected: all green.

- [ ] **Step 5: Commit**

```
git add src/Fleet/Shared/ShellLine.cs src/Fleet/Platform/Mux/WezTerm/EnvLaunch.cs tests/Fleet.Tests/Shared/ShellLineTests.cs && git commit -q -m "feat: ShellLine wraps one line for cmd or sh; EnvLaunch reuses it"
```

### Task 3: RunProfile, IRunStore, JsonRunStore

**Files:**
- Create: `src/Fleet/Ports/Runs/Models/RunProfile.cs`
- Create: `src/Fleet/Ports/Runs/IRunStore.cs`
- Create: `src/Fleet/Platform/Storage/Models/RunsFile.cs`
- Create: `src/Fleet/Platform/Storage/JsonRunStore.cs`
- Modify: `src/Fleet/Platform/Storage/FleetJsonContext.cs` (add `[JsonSerializable(typeof(RunsFile))]`)
- Modify: `src/Fleet/Platform/Storage/FleetPaths.cs` (add `Runs`, create it in `EnsureDirs`)
- Test: `tests/Fleet.Tests/Platform/Storage/JsonRunStoreTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Platform.Storage;
using Fleet.Ports.Runs.Models;

namespace Fleet.Tests.Platform.Storage;

[Collection(ConfigHomeCollection.Name)]
public sealed class JsonRunStoreTests : ConfigHomeFixture
{
    private readonly JsonRunStore _store = new();

    [Fact]
    public void A_project_with_no_file_has_no_profiles()
    {
        Assert.Empty(_store.List("techweb"));
    }

    [Fact]
    public void A_saved_profile_comes_back_and_its_url_is_derived()
    {
        _store.Save("techweb", new RunProfile("frontend", "npm run dev", 5173, "/app"));

        var profile = Assert.Single(_store.List("techweb"));

        Assert.Equal("frontend", profile.Repository);
        Assert.Equal("npm run dev", profile.Command);
        Assert.Equal(5173, profile.Port);
        Assert.Equal("/app", profile.Path);
        Assert.Equal("http://localhost:5173/app", profile.Url);
    }

    [Fact]
    public void Saving_again_replaces_the_profile_for_that_repository_case_insensitively()
    {
        _store.Save("techweb", new RunProfile("frontend", "npm run dev", 5173, "/"));
        _store.Save("techweb", new RunProfile("Frontend", "npm start", 3000, "/"));

        var profile = Assert.Single(_store.List("techweb"));

        Assert.Equal(3000, profile.Port);
    }

    [Fact]
    public void Removing_forgets_only_that_repository()
    {
        _store.Save("techweb", new RunProfile("frontend", "npm run dev", 5173, "/"));
        _store.Save("techweb", new RunProfile("backend", "dotnet run", 5000, "/"));

        _store.Remove("techweb", "frontend");

        Assert.Equal("backend", Assert.Single(_store.List("techweb")).Repository);
    }

    [Fact]
    public void Profiles_do_not_leak_between_projects()
    {
        _store.Save("techweb", new RunProfile("frontend", "npm run dev", 5173, "/"));

        Assert.Empty(_store.List("other"));
    }

    [Fact]
    public void The_file_is_a_versioned_object_under_runs()
    {
        _store.Save("techweb", new RunProfile("frontend", "npm run dev", 5173, "/"));

        var file = Path.Combine(ConfigHome, "runs", "techweb.json");

        Assert.True(File.Exists(file));
        Assert.Contains("\"version\": 1", File.ReadAllText(file));
        Assert.Contains("\"profiles\"", File.ReadAllText(file));
    }
}
```

- [ ] **Step 2: Run it, expect compile failure.**

- [ ] **Step 3: Implement the port**

`src/Fleet/Ports/Runs/Models/RunProfile.cs`:

```csharp
namespace Fleet.Ports.Runs.Models;

public sealed record RunProfile(string Repository, string Command, int Port, string Path)
{
    public const string DefaultPath = "/";

    public string Url => $"http://localhost:{Port}{Path}";
}
```

`src/Fleet/Ports/Runs/IRunStore.cs`:

```csharp
using Fleet.Ports.Runs.Models;

namespace Fleet.Ports.Runs;

public interface IRunStore
{
    IReadOnlyList<RunProfile> List(string project);

    void Save(string project, RunProfile profile);

    void Remove(string project, string repository);
}
```

- [ ] **Step 4: Implement the platform store**

`src/Fleet/Platform/Storage/Models/RunsFile.cs`:

```csharp
namespace Fleet.Platform.Storage.Models;

public sealed class RunsFile
{
    public int Version { get; set; } = 1;

    public List<RunEntry> Profiles { get; set; } = [];
}

public sealed class RunEntry
{
    public string Repository { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public int Port { get; set; }

    public string Path { get; set; } = "/";
}
```

In `FleetJsonContext.cs` add `[JsonSerializable(typeof(RunsFile))]` after the `SettingsFile` line.

In `FleetPaths.cs` add `public static string Runs => Path.Combine(Config, "runs");` after `Approvals`, and `Directory.CreateDirectory(Runs);` inside `EnsureDirs`.

`src/Fleet/Platform/Storage/JsonRunStore.cs` — same lock/read/write shape as `JsonAgentStore`:

```csharp
using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Runs;
using Fleet.Ports.Runs.Models;
using Fleet.Shared;

namespace Fleet.Platform.Storage;

public sealed class JsonRunStore : IRunStore
{
    public IReadOnlyList<RunProfile> List(string project)
    {
        var file = FileFor(project);

        if (file is null)
        {
            return [];
        }

        return Read(file).Profiles
            .Select(p => new RunProfile(p.Repository, p.Command, p.Port, p.Path))
            .OrderBy(p => p.Repository, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Save(string project, RunProfile profile)
    {
        var file = FileFor(project);

        if (file is null || profile.Repository.Length == 0)
        {
            return;
        }

        Mutate(file, runs =>
        {
            runs.Profiles.RemoveAll(p => Same(p.Repository, profile.Repository));
            runs.Profiles.Add(new RunEntry
            {
                Repository = profile.Repository,
                Command = profile.Command,
                Port = profile.Port,
                Path = profile.Path,
            });
        });
    }

    public void Remove(string project, string repository)
    {
        var file = FileFor(project);

        if (file is null)
        {
            return;
        }

        Mutate(file, runs => runs.Profiles.RemoveAll(p => Same(p.Repository, repository)));
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void Mutate(string file, Action<RunsFile> change)
    {
        FleetPaths.EnsureDirs();

        using var gate = Lock(file + ".lock");

        var runs = Read(file);
        change(runs);
        Write(file, runs);
    }

    private static FileStream? Lock(string lockPath)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (true)
        {
            try
            {
                return new FileStream(
                    lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(20);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    private static RunsFile Read(string file)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (!File.Exists(file))
                {
                    return new RunsFile();
                }

                return JsonSerializer.Deserialize(
                    File.ReadAllText(file), FleetJsonContext.Default.RunsFile) ?? new RunsFile();
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(15);
            }
            catch (Exception e)
                when (e is IOException or JsonException or UnauthorizedAccessException)
            {
                return new RunsFile();
            }
        }
    }

    private static void Write(string file, RunsFile runs)
    {
        try
        {
            FleetPaths.EnsureDirs();

            var temp = file + ".tmp";

            File.WriteAllText(temp, JsonSerializer.Serialize(runs, FleetJsonContext.Default.RunsFile));
            File.Move(temp, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string? FileFor(string project)
    {
        var name = ProjectName.Sanitize(project);

        return name.Length == 0 ? null : Path.Combine(FleetPaths.Runs, name + ".json");
    }
}
```

- [ ] **Step 5: Run the full suite. Expected: green, 6 new tests.**

- [ ] **Step 6: Commit**

```
git add src/Fleet/Ports/Runs src/Fleet/Platform/Storage tests/Fleet.Tests/Platform/Storage/JsonRunStoreTests.cs && git commit -q -m "feat: run profiles per repository, stored under runs/<project>.json"
```

### Task 4: RunPanes and RunStatus (area root)

**Files:**
- Create: `src/Fleet/Features/Repositories/RunPanes.cs`
- Create: `src/Fleet/Features/Repositories/RunStatus.cs`
- Test: `tests/Fleet.Tests/Features/Repositories/RunStatusTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Features.Repositories;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Runs.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Repositories;

public class RunStatusTests
{
    private static readonly RunProfile Profile = new("frontend", "npm run dev", 5173, "/");

    private static Pane Pane(string id, string title, string cwd = "C:/repos/techweb/frontend/develop") =>
        new(new PaneId(id), "w1", "t" + id, "default", title, cwd, false);

    [Fact]
    public void No_profile_and_no_pane_is_not_running_and_has_no_url()
    {
        var status = RunStatus.For("frontend", null, []);

        Assert.False(status.Running);
        Assert.Null(status.Url);
    }

    [Fact]
    public void A_profile_without_a_pane_is_not_running_but_knows_its_url()
    {
        var status = RunStatus.For("frontend", Profile, [Pane("1", "frontend/develop")]);

        Assert.False(status.Running);
        Assert.Equal("http://localhost:5173/", status.Url);
    }

    [Fact]
    public void A_run_pane_titled_after_the_repository_means_running()
    {
        var status = RunStatus.For("frontend", Profile, [Pane("1", FleetTabTitles.Run("frontend"))]);

        Assert.True(status.Running);
        Assert.Equal("http://localhost:5173/", status.Url);
    }

    [Fact]
    public void A_run_pane_with_no_profile_is_running_with_no_url()
    {
        var status = RunStatus.For("frontend", null, [Pane("1", "frontend run")]);

        Assert.True(status.Running);
        Assert.Null(status.Url);
    }

    [Fact]
    public void Another_repositorys_run_pane_does_not_count()
    {
        Assert.False(RunStatus.For("frontend", Profile, [Pane("1", "backend run")]).Running);
    }

    [Fact]
    public void Ownership_is_by_title_not_cwd()
    {
        var byCwdOnly = Pane("1", "frontend/develop");
        var byTitle = Pane("2", "frontend run", cwd: string.Empty);

        Assert.False(RunPanes.Owns(byCwdOnly, "frontend"));
        Assert.True(RunPanes.Owns(byTitle, "frontend"));
        Assert.True(RunPanes.Owns(Pane("3", "FRONTEND RUN"), "frontend"));
    }
}
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

`src/Fleet/Features/Repositories/RunPanes.cs`:

```csharp
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Features.Repositories;

public static class RunPanes
{
    public static bool Owns(Pane pane, string repository) =>
        string.Equals(pane.Title, FleetTabTitles.Run(repository), StringComparison.OrdinalIgnoreCase);
}
```

`src/Fleet/Features/Repositories/RunStatus.cs`:

```csharp
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Runs.Models;

namespace Fleet.Features.Repositories;

public sealed record RunStatus(bool Running, string? Url)
{
    public static RunStatus For(string repository, RunProfile? profile, IReadOnlyList<Pane> panes) =>
        new(panes.Any(p => RunPanes.Owns(p, repository)), profile?.Url);
}
```

- [ ] **Step 4: Run the full suite. Expected: green.**

- [ ] **Step 5: Commit**

```
git add src/Fleet/Features/Repositories/RunPanes.cs src/Fleet/Features/Repositories/RunStatus.cs tests/Fleet.Tests/Features/Repositories/RunStatusTests.cs && git commit -q -m "feat: run status is derived from live panes by tab title"
```

### Task 5: Every cwd matcher skips run panes

**Files:**
- Modify: `src/Fleet/Ports/Agents/AgentPaneMatch.cs`
- Modify: `src/Fleet/Features/Repositories/OpenRepository/OpenRepositoryHandler.cs`
- Modify: `src/Fleet/Features/Agents/StopAgent/StopAgentHandler.cs`
- Modify: `src/Fleet/Features/Agents/RemoveAgent/RemoveAgentHandler.cs` (`StopAsync`)
- Modify: `src/Fleet/Features/Projects/RestoreSession/RestoreSessionHandler.cs` (`Wanted`)
- Test: `tests/Fleet.Tests/Features/Agents/AgentPanesTests.cs` (add one test)
- Test: `tests/Fleet.Tests/Features/Repositories/OpenRepositoryTests.cs` (add one test; add `using Fleet.Shared;` and `using Fleet.Shared.Constants;`)
- Test: `tests/Fleet.Tests/Features/Agents/StopAgentTests.cs` (create)
- Test: `tests/Fleet.Tests/Features/Agents/RemoveAgentTests.cs` (add one test)
- Test: `tests/Fleet.Tests/Features/Projects/RestoreSessionTests.cs` (add one test)

Five places match an agent's pane by cwd alone. Hide and open already go through
`AgentPaneMatch.Owns`; stop, remove and restore-session do not. All five must
ignore a run pane, otherwise stopping an agent on the default branch kills the
dev server, and restoring a session thinks the agent is open when only the
server is.

- [ ] **Step 1: Add the failing tests**

In `AgentPanesTests.cs` add:

```csharp
    [Fact]
    public void A_run_tab_is_never_an_agents_even_at_the_agents_worktree()
    {
        Assert.False(AgentPanes.Owns(Pane(FleetTabTitles.Run("backend"), Sub.Worktree), Sub));
    }
```

In `OpenRepositoryTests.cs` add (inside the class; `Worktree`, `ProjectRoot`, `_mux` already exist there):

```csharp
    [Fact]
    public async Task A_run_pane_in_the_worktree_does_not_count_as_the_repository_being_open()
    {
        var worktree = Worktree("frontend", "develop");
        await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot });
        var server = await _mux.SpawnAsync(new SpawnOptions { Cwd = worktree, Args = ["cmd", "/c", "npm run dev"] });
        await _mux.SetTitleAsync(server, Fleet.Shared.Constants.FleetTabTitles.Run("frontend"));

        var result = await new OpenRepositoryHandler(_mux).HandleAsync(
            "techweb", "frontend", Path.Combine(ProjectRoot, "frontend"), "develop", ProjectRoot);

        Assert.True(result.Succeeded, result.Error);

        var panes = await _mux.ListPanesAsync();

        Assert.Equal(3, panes.Count);
        Assert.Contains(panes, p => p.Id != server && PathKey.Same(p.Cwd, worktree));
    }
```

(Use `FleetTabTitles.Run("frontend")` instead of the fully qualified name once the two usings are added.)

New file `tests/Fleet.Tests/Features/Agents/StopAgentTests.cs`:

```csharp
using Fleet.Features.Agents.StopAgent;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Agents;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Mux.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Agents;

public class StopAgentTests
{
    private readonly FakeMuxDriver _mux = new();

    private readonly RecordingStore _store = new();

    private static readonly AgentRecord Agent = new(
        "C:/repos/techweb/backend/develop", "backend", "develop", AgentHarness.Nvim,
        "origin/develop", true, Hidden: false, Open: true);

    [Fact]
    public async Task Stopping_kills_the_agents_pane_but_spares_a_run_pane_in_the_same_worktree()
    {
        var editor = await _mux.SpawnAsync(new SpawnOptions { Cwd = Agent.Worktree, NewWindow = true });
        await _mux.SetTitleAsync(editor, AgentTitle.For(Agent.Repository, Agent.Branch));
        var server = await _mux.SpawnAsync(new SpawnOptions { Cwd = Agent.Worktree, NewWindow = true });
        await _mux.SetTitleAsync(server, FleetTabTitles.Run("backend"));

        var result = await new StopAgentHandler(_mux, _store).HandleAsync("techweb", Agent);

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal([server], (await _mux.ListPanesAsync()).Select(p => p.Id));
        Assert.False(Assert.Single(_store.Saved).Open);
    }

    [Fact]
    public async Task A_hidden_pane_titled_after_the_agent_counts_as_running_even_when_its_cwd_is_blank()
    {
        var hidden = await _mux.SpawnAsync(new SpawnOptions { Cwd = string.Empty, NewWindow = true });
        await _mux.SetTitleAsync(hidden, AgentTitle.For(Agent.Repository, Agent.Branch));

        var result = await new StopAgentHandler(_mux, _store).HandleAsync("techweb", Agent);

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(await _mux.ListPanesAsync());
    }

    private sealed class RecordingStore : IAgentStore
    {
        public List<AgentRecord> Saved { get; } = [];

        public void Save(string project, AgentRecord agent) => Saved.Add(agent);

        public IReadOnlyList<AgentRecord> List(string project) => Saved;

        public void Remove(string project, string worktree) { }
    }
}
```

In `RemoveAgentTests.cs`, after `The_agents_pane_is_killed_before_the_worktree_goes`, add:

```csharp
    [Fact]
    public async Task A_run_pane_in_the_agents_worktree_survives_the_removal()
    {
        var agent = await AgentAsync();
        await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        var server = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        await _mux.SetTitleAsync(server, FleetTabTitles.Run(agent.Repository));

        await Handler().HandleAsync("techweb", agent, deleteWorktree: false);

        Assert.Equal([server], (await _mux.ListPanesAsync()).Select(p => p.Id));
    }
```

`deleteWorktree: false` because the run pane's cwd is inside the worktree; on Windows a live process there would block deletion and turn this into a filesystem test.

In `RestoreSessionTests.cs`, after `An_agent_that_is_already_running_is_left_alone`, add:

```csharp
    [Fact]
    public async Task A_run_pane_in_the_worktree_does_not_make_the_agent_look_open()
    {
        var agent = Agent("develop", open: true);
        var server = await _mux.SpawnAsync(new SpawnOptions { Cwd = agent.Worktree });
        await _mux.SetTitleAsync(server, FleetTabTitles.Run("backend"));

        var restored = await new RestoreSessionHandler(_mux)
            .HandleAsync("techweb", ProjectRoot, [agent]);

        Assert.Equal(1, restored);
        Assert.Equal(2, (await _mux.ListPanesAsync()).Count);
    }
```

- [ ] **Step 2: Run the touched classes, expect the five new tests to FAIL**

Run: `cd C:/repos/fleet/tests/Fleet.Tests && dotnet test --filter "FullyQualifiedName~AgentPanesTests|FullyQualifiedName~OpenRepositoryTests|FullyQualifiedName~StopAgentTests|FullyQualifiedName~RemoveAgentTests|FullyQualifiedName~RestoreSessionTests" 2>&1 | grep -E "error CS|\[FAIL\]|Passed!|Failed!"`
Expected: `Failed:     5` (the run pane is claimed, killed, or mistaken for the agent).

- [ ] **Step 3: Implement**

`AgentPaneMatch.Owns` becomes:

```csharp
    public static bool Owns(Pane pane, AgentRecord agent) =>
        !string.Equals(pane.Title, FleetTabTitles.Dashboard, StringComparison.OrdinalIgnoreCase)
        && !FleetTabTitles.IsRun(pane.Title)
        && (PathKey.Same(pane.Cwd, agent.Worktree)
            || string.Equals(
                pane.Title,
                AgentTitle.For(agent.Repository, agent.Branch),
                StringComparison.OrdinalIgnoreCase)
            || string.Equals(pane.Title, BrowserTitle(agent), StringComparison.OrdinalIgnoreCase));
```

In `OpenRepositoryHandler.HandleAsync` change the `open` lookup to:

```csharp
        var open = panes.FirstOrDefault(p =>
            PathKey.Same(p.Cwd, directory) && !FleetTabTitles.IsRun(p.Title));
```

In `StopAgentHandler.HandleAsync` replace the `running` line with:

```csharp
        var running = panes.Where(p => AgentPanes.Owns(p, agent)).ToList();
```

(`AgentPanes` is in the parent namespace `Fleet.Features.Agents`; drop the now unused `using Fleet.Shared;` if the compiler warns.)

In `RemoveAgentHandler.StopAsync` replace the `Where` predicate with `p => AgentPanes.Owns(p, agent)`.

In `RestoreSessionHandler.Wanted` replace the last clause with
`&& !panes.Any(p => AgentPaneMatch.Owns(p, agent))` and add `using Fleet.Ports.Agents;`.
`Fleet.Features.Projects` may not reference `Fleet.Features.Agents`, so this one goes
through the Ports helper that `AgentPanes` itself delegates to.

- [ ] **Step 4: Run the full suite. Expected: green.**

- [ ] **Step 5: Commit**

```
git add src/Fleet/Ports/Agents/AgentPaneMatch.cs src/Fleet/Features/Repositories/OpenRepository/OpenRepositoryHandler.cs src/Fleet/Features/Agents/StopAgent/StopAgentHandler.cs src/Fleet/Features/Agents/RemoveAgent/RemoveAgentHandler.cs src/Fleet/Features/Projects/RestoreSession/RestoreSessionHandler.cs tests/Fleet.Tests/Features/Agents tests/Fleet.Tests/Features/Repositories/OpenRepositoryTests.cs tests/Fleet.Tests/Features/Projects/RestoreSessionTests.cs && git commit -q -m "fix: run panes are never mistaken for an agent or an open repository"
```

---

## Chunk 2: The three handlers

All three live in `src/Fleet/Features/Repositories/RunRepository/`. Tests use `FakeMuxDriver` and an in-memory `IRunStore` defined once in `tests/Fleet.Tests/Features/Repositories/MemoryRunStore.cs`.

### Task 6: MemoryRunStore test double and SetRunCommandHandler

**Files:**
- Create: `tests/Fleet.Tests/Features/Repositories/MemoryRunStore.cs`
- Create: `src/Fleet/Features/Repositories/RunRepository/SetRunCommandHandler.cs`
- Test: `tests/Fleet.Tests/Features/Repositories/SetRunCommandTests.cs` (create)

- [ ] **Step 1: Write the test double**

```csharp
using Fleet.Ports.Runs;
using Fleet.Ports.Runs.Models;

namespace Fleet.Tests.Features.Repositories;

public sealed class MemoryRunStore : IRunStore
{
    private readonly List<RunProfile> _profiles = [];

    public IReadOnlyList<RunProfile> List(string project) => _profiles.ToList();

    public void Save(string project, RunProfile profile)
    {
        Remove(project, profile.Repository);
        _profiles.Add(profile);
    }

    public void Remove(string project, string repository) =>
        _profiles.RemoveAll(p => string.Equals(p.Repository, repository, StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Fleet.Features.Repositories.RunRepository;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Runs.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Repositories;

public class SetRunCommandTests
{
    private readonly MemoryRunStore _store = new();

    private static readonly string[] Repositories = ["frontend", "backend"];

    private static Pane RunPane(string repository) =>
        new(new PaneId("9"), "w1", "t9", "default", FleetTabTitles.Run(repository), string.Empty, false);

    private SetRunCommandHandler Handler => new(_store);

    [Fact]
    public void A_valid_profile_is_saved_with_the_path_defaulting_to_root()
    {
        var result = Handler.Handle("techweb", Repositories, [], new RunProfile("frontend", " npm run dev ", 5173, ""));

        Assert.True(result.Succeeded, result.Error);
        var saved = Assert.Single(_store.List("techweb"));
        Assert.Equal("npm run dev", saved.Command);
        Assert.Equal("/", saved.Path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    [InlineData(-1)]
    public void A_port_outside_the_tcp_range_is_refused(int port)
    {
        var result = Handler.Handle("techweb", Repositories, [], new RunProfile("frontend", "npm run dev", port, "/"));

        Assert.False(result.Succeeded);
        Assert.Contains("port", result.Error);
        Assert.Empty(_store.List("techweb"));
    }

    [Theory]
    [InlineData("app")]
    [InlineData("/a b")]
    public void A_path_must_start_with_a_slash_and_carry_no_whitespace(string path)
    {
        var result = Handler.Handle("techweb", Repositories, [], new RunProfile("frontend", "npm run dev", 5173, path));

        Assert.False(result.Succeeded);
        Assert.Contains("path", result.Error);
    }

    [Fact]
    public void An_unknown_repository_is_refused()
    {
        var result = Handler.Handle("techweb", Repositories, [], new RunProfile("docs", "mkdocs serve", 8000, "/"));

        Assert.False(result.Succeeded);
        Assert.Contains("docs", result.Error);
    }

    [Fact]
    public void An_empty_command_deletes_the_profile()
    {
        _store.Save("techweb", new RunProfile("frontend", "npm run dev", 5173, "/"));

        var result = Handler.Handle("techweb", Repositories, [], new RunProfile("frontend", "  ", 5173, "/"));

        Assert.True(result.Succeeded, result.Error);
        Assert.Empty(_store.List("techweb"));
        Assert.Contains("removed", result.Value);
    }

    [Fact]
    public void Nothing_changes_while_the_repository_is_running()
    {
        _store.Save("techweb", new RunProfile("frontend", "npm run dev", 5173, "/"));

        var result = Handler.Handle("techweb", Repositories, [RunPane("frontend")], new RunProfile("frontend", "npm start", 3000, "/"));

        Assert.False(result.Succeeded);
        Assert.Contains("stop frontend first", result.Error);
        Assert.Equal(5173, Assert.Single(_store.List("techweb")).Port);
    }
}
```

- [ ] **Step 3: Run, expect compile failure.**

- [ ] **Step 4: Implement**

`src/Fleet/Features/Repositories/RunRepository/SetRunCommandHandler.cs`:

```csharp
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Runs;
using Fleet.Ports.Runs.Models;
using Fleet.Shared.Results;

namespace Fleet.Features.Repositories.RunRepository;

public sealed class SetRunCommandHandler(IRunStore store)
{
    public Result<string> Handle(
        string project,
        IReadOnlyList<string> repositories,
        IReadOnlyList<Pane> panes,
        RunProfile wanted)
    {
        var repository = wanted.Repository.Trim();

        if (!repositories.Any(r => string.Equals(r, repository, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<string>.Fail($"{repository} is not a repository of this project.");
        }

        if (panes.Any(p => RunPanes.Owns(p, repository)))
        {
            return Result<string>.Fail($"stop {repository} first; its run command cannot change while it runs.");
        }

        var command = wanted.Command.Trim();

        if (command.Length == 0)
        {
            store.Remove(project, repository);

            return Result<string>.Ok($"{repository}: run command removed.");
        }

        if (wanted.Port is < 1 or > 65535)
        {
            return Result<string>.Fail($"port {wanted.Port} is outside 1-65535.");
        }

        var path = wanted.Path.Trim().Length == 0 ? RunProfile.DefaultPath : wanted.Path.Trim();

        if (!path.StartsWith('/') || path.Any(char.IsWhiteSpace))
        {
            return Result<string>.Fail($"path '{path}' must start with / and contain no whitespace.");
        }

        var profile = new RunProfile(repository, command, wanted.Port, path);

        store.Save(project, profile);

        return Result<string>.Ok($"{repository} runs '{command}' at {profile.Url}.");
    }
}
```

- [ ] **Step 5: Run the full suite. Expected: green.**

- [ ] **Step 6: Commit**

```
git add src/Fleet/Features/Repositories/RunRepository/SetRunCommandHandler.cs tests/Fleet.Tests/Features/Repositories/MemoryRunStore.cs tests/Fleet.Tests/Features/Repositories/SetRunCommandTests.cs && git commit -q -m "feat: set or clear a repository's run command with validation"
```

### Task 7: RunRepositoryHandler and PortProbe

**Files:**
- Create: `src/Fleet/Features/Repositories/RunRepository/PortProbe.cs`
- Create: `src/Fleet/Features/Repositories/RunRepository/RunRepositoryHandler.cs`
- Test: `tests/Fleet.Tests/Features/Repositories/RunRepositoryTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

```csharp
using Fleet.Features.Repositories.RunRepository;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Runs.Models;
using Fleet.Shared;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Repositories;

public sealed class RunRepositoryTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());

    private readonly FakeMuxDriver _mux = new();

    private readonly MemoryRunStore _store = new();

    private readonly HashSet<int> _boundPorts = [];

    public RunRepositoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string ProjectRoot => Path.Combine(_root, "techweb");

    private string Worktree()
    {
        var path = Path.Combine(ProjectRoot, "frontend", "develop");
        Directory.CreateDirectory(path);
        return path;
    }

    private static readonly RunProfile Profile = new("frontend", "npm run dev", 5173, "/");

    private RunRepositoryHandler Handler(bool windows = true) =>
        new(_mux, _store, port => _boundPorts.Contains(port), windows);

    private Task<Fleet.Shared.Results.Result<string>> RunAsync(bool windows = true) =>
        Handler(windows).HandleAsync(
            "techweb", ProjectRoot, "frontend", Path.Combine(ProjectRoot, "frontend"), "develop");

    [Fact]
    public async Task It_spawns_the_command_in_the_worktree_in_the_project_window_and_titles_the_tab()
    {
        var worktree = Worktree();
        _store.Save("techweb", Profile);
        var dashboard = await _mux.SpawnAsync(new SpawnOptions { Cwd = ProjectRoot, NewWindow = true });
        var home = (await _mux.ListPanesAsync()).Single(p => p.Id == dashboard).WindowId;

        var result = await RunAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.Equal("http://localhost:5173/", result.Value);

        var pane = (await _mux.ListPanesAsync()).Single(p => p.Id != dashboard);

        Assert.Equal(home, pane.WindowId);
        Assert.True(PathKey.Same(pane.Cwd, worktree));
        Assert.Equal("techweb", pane.SessionName);
        Assert.Equal(FleetTabTitles.Run("frontend"), pane.Title);
        Assert.Equal(["cmd", "/c", "npm run dev"], _mux.ArgsFor(pane.Id));
        Assert.Empty(_mux.EnvFor(pane.Id));
    }

    [Fact]
    public async Task On_linux_the_command_runs_under_sh()
    {
        Worktree();
        _store.Save("techweb", Profile);

        await RunAsync(windows: false);

        var pane = Assert.Single(await _mux.ListPanesAsync());
        Assert.Equal(["sh", "-c", "npm run dev"], _mux.ArgsFor(pane.Id));
    }

    [Fact]
    public async Task Without_a_dashboard_window_it_opens_a_new_one()
    {
        Worktree();
        _store.Save("techweb", Profile);

        var result = await RunAsync();

        Assert.True(result.Succeeded, result.Error);
        Assert.Single(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task It_refuses_without_a_profile()
    {
        Worktree();

        var result = await RunAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("no run command for frontend", result.Error);
        Assert.Empty(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task It_refuses_when_already_running()
    {
        var worktree = Worktree();
        _store.Save("techweb", Profile);
        var running = await _mux.SpawnAsync(new SpawnOptions { Cwd = worktree });
        await _mux.SetTitleAsync(running, FleetTabTitles.Run("frontend"));

        var result = await RunAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("already running at http://localhost:5173/", result.Error);
        Assert.Single(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task It_refuses_when_the_port_is_bound()
    {
        Worktree();
        _store.Save("techweb", Profile);
        _boundPorts.Add(5173);

        var result = await RunAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("port 5173 is in use", result.Error);
        Assert.Empty(await _mux.ListPanesAsync());
    }

    [Fact]
    public async Task It_refuses_when_the_worktree_is_gone()
    {
        _store.Save("techweb", Profile);

        var result = await RunAsync();

        Assert.False(result.Succeeded);
        Assert.Contains("is gone", result.Error);
    }
}
```

Note: when the worktree directory does not exist, `RepositoryWorktree.For` falls back to the container `ProjectRoot/frontend`, which also does not exist in that test, so "is gone" fires.

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

`src/Fleet/Features/Repositories/RunRepository/PortProbe.cs` — a bind attempt on the
loopback address. Plain sockets, no `/proc` parsing, no reflection: identical on
Windows and Linux and safe under NativeAOT. A server bound to any address on that
port makes the bind fail, which is exactly "in use".

```csharp
using System.Net;
using System.Net.Sockets;

namespace Fleet.Features.Repositories.RunRepository;

public static class PortProbe
{
    public static bool InUse(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);

        try
        {
            listener.Start();

            return false;
        }
        catch (SocketException)
        {
            return true;
        }
        finally
        {
            listener.Stop();
        }
    }
}
```

`src/Fleet/Features/Repositories/RunRepository/RunRepositoryHandler.cs`:

```csharp
using Fleet.Ports.Mux;
using Fleet.Ports.Mux.Models;
using Fleet.Ports.Runs;
using Fleet.Shared;
using Fleet.Shared.Constants;
using Fleet.Shared.Results;

namespace Fleet.Features.Repositories.RunRepository;

public sealed class RunRepositoryHandler(
    IMuxDriver mux, IRunStore store, Func<int, bool> portInUse, bool windows)
{
    public async Task<Result<string>> HandleAsync(
        string project,
        string projectRoot,
        string repository,
        string container,
        string defaultBranch,
        CancellationToken ct = default)
    {
        var profile = store.List(project).FirstOrDefault(p =>
            string.Equals(p.Repository, repository, StringComparison.OrdinalIgnoreCase));

        if (profile is null)
        {
            return Result<string>.Fail($"no run command for {repository}; set one first.");
        }

        var directory = RepositoryWorktree.For(container, defaultBranch, Directory.Exists);

        if (!Directory.Exists(directory))
        {
            return Result<string>.Fail($"{directory} is gone.");
        }

        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        if (panes.Any(p => RunPanes.Owns(p, repository)))
        {
            return Result<string>.Fail($"{repository} is already running at {profile.Url}.");
        }

        if (portInUse(profile.Port))
        {
            return Result<string>.Fail($"port {profile.Port} is in use.");
        }

        var window = panes.FirstOrDefault(p => PathKey.Same(p.Cwd, projectRoot))?.WindowId;

        var pane = await mux.SpawnAsync(
            new SpawnOptions
            {
                Cwd = directory,
                SessionName = project,
                WindowId = window,
                NewWindow = window is null,
                Args = ShellLine.Command(windows, profile.Command),
            },
            ct).ConfigureAwait(false);

        if (pane.IsNone)
        {
            return Result<string>.Fail($"the {mux.Name} multiplexer did not respond. Run 'fleet doctor'.");
        }

        await mux.SetTitleAsync(pane, FleetTabTitles.Run(repository), ct).ConfigureAwait(false);

        return Result<string>.Ok(profile.Url);
    }
}
```

- [ ] **Step 4: Run the full suite. Expected: green.**

- [ ] **Step 5: Commit**

```
git add src/Fleet/Features/Repositories/RunRepository tests/Fleet.Tests/Features/Repositories/RunRepositoryTests.cs && git commit -q -m "feat: run a repository's command in a titled pane of the project window"
```

### Task 8: StopRunHandler

**Files:**
- Create: `src/Fleet/Features/Repositories/RunRepository/StopRunHandler.cs`
- Test: `tests/Fleet.Tests/Features/Repositories/StopRunTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

```csharp
using Fleet.Features.Repositories.RunRepository;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux.Models;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Features.Repositories;

public class StopRunTests
{
    private readonly FakeMuxDriver _mux = new();

    [Fact]
    public async Task Every_pane_titled_after_the_run_is_killed_and_nothing_else()
    {
        var one = await _mux.SpawnAsync(new SpawnOptions { Cwd = "C:/x", NewWindow = true });
        await _mux.SetTitleAsync(one, FleetTabTitles.Run("frontend"));
        var two = await _mux.SpawnAsync(new SpawnOptions { Cwd = "C:/x", NewWindow = true });
        await _mux.SetTitleAsync(two, FleetTabTitles.Run("frontend"));
        var other = await _mux.SpawnAsync(new SpawnOptions { Cwd = "C:/x", NewWindow = true });
        await _mux.SetTitleAsync(other, FleetTabTitles.Run("backend"));
        var editor = await _mux.SpawnAsync(new SpawnOptions { Cwd = "C:/x", NewWindow = true });
        await _mux.SetTitleAsync(editor, "frontend/develop");

        var result = await new StopRunHandler(_mux).HandleAsync("frontend");

        Assert.True(result.Succeeded, result.Error);

        Assert.Equal([other, editor], (await _mux.ListPanesAsync()).Select(p => p.Id));
    }

    [Fact]
    public async Task Stopping_what_is_not_running_fails_plainly()
    {
        var result = await new StopRunHandler(_mux).HandleAsync("frontend");

        Assert.False(result.Succeeded);
        Assert.Equal("frontend is not running.", result.Error);
    }
}
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

```csharp
using Fleet.Ports.Mux;
using Fleet.Shared.Results;

namespace Fleet.Features.Repositories.RunRepository;

public sealed class StopRunHandler(IMuxDriver mux)
{
    public async Task<Result> HandleAsync(string repository, CancellationToken ct = default)
    {
        var mine = (await mux.ListPanesAsync(ct).ConfigureAwait(false))
            .Where(p => RunPanes.Owns(p, repository))
            .ToList();

        if (mine.Count == 0)
        {
            return Result.Fail($"{repository} is not running.");
        }

        foreach (var pane in mine)
        {
            await mux.KillPaneAsync(pane.Id, ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }
}
```

- [ ] **Step 4: Run the full suite. Expected: green.**

- [ ] **Step 5: Commit**

```
git add src/Fleet/Features/Repositories/RunRepository/StopRunHandler.cs tests/Fleet.Tests/Features/Repositories/StopRunTests.cs && git commit -q -m "feat: stop a running repository by killing its run panes"
```

---

## Chunk 3: Surfaces — MCP, chores, rows, wiring

### Task 9: MCP tools for agents

**Files:**
- Modify: `src/Fleet/Shared/Settings/Enums/HarnessTool.cs` (append `RunRepository, StopRun, RunStatus, SetRunCommand`)
- Modify: `src/Fleet/Shared/Settings/HarnessToolIds.cs`
- Modify: `src/Fleet/Shared/Settings/SettingsDefaults.cs`
- Modify: `src/Fleet/Features/Mcp/ServeMcp/ToolArguments.cs`
- Modify: `src/Fleet/Features/Mcp/ServeMcp/McpTools.cs`
- Modify: `src/Fleet/Cli/Composition/McpActions.cs`
- Modify: `src/Fleet/Cli/Composition/McpWiring.cs`
- Modify: `src/Fleet/Cli/Composition/Adapters.cs` (add `Runs()`)
- Test: `tests/Fleet.Tests/Shared/SettingsTests.cs`, `tests/Fleet.Tests/Features/Mcp/McpToolsTests.cs`

- [ ] **Step 1: Add failing tests**

In `SettingsTests.The_shipped_defaults_match_the_intended_policy_per_tool`, add after the `Dispatch` line:

```csharp
        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.RunRepository).Policy);
        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.StopRun).Policy);
        Assert.Equal(ActionPolicy.Allow, config.RuleFor(HarnessTool.RunStatus).Policy);
        Assert.Equal(ActionPolicy.Ask, config.RuleFor(HarnessTool.SetRunCommand).Policy);
```

In `McpToolsTests` add:

```csharp
    [Fact]
    public void Set_run_command_takes_a_command_and_a_port_and_an_optional_path()
    {
        var spec = McpTools.All.Single(s => s.Tool == HarnessTool.SetRunCommand);

        Assert.Contains(spec.Params, p => p.Name == "repository" && p.Required);
        Assert.Contains(spec.Params, p => p.Name == "command" && p.Required && p.Type == "string");
        Assert.Contains(spec.Params, p => p.Name == "port" && p.Required && p.Type == "integer");
        Assert.Contains(spec.Params, p => p.Name == "path" && !p.Required);
    }

    [Fact]
    public void The_run_tools_are_exposed_with_their_wire_ids()
    {
        Assert.Equal("run_repository", HarnessToolIds.For(HarnessTool.RunRepository));
        Assert.Equal("stop_run", HarnessToolIds.For(HarnessTool.StopRun));
        Assert.Equal("run_status", HarnessToolIds.For(HarnessTool.RunStatus));
        Assert.Equal("set_run_command", HarnessToolIds.For(HarnessTool.SetRunCommand));
        Assert.NotNull(McpTools.Find("run_status"));
    }
```

The existing `Every_configurable_tool_is_exposed_over_mcp` and `Every_tool_has_a_wire_id_that_round_trips_and_none_collide` tests will also fail until every switch is updated. That is intended: they are the checklist.

- [ ] **Step 2: Run the suite, expect failures in `SettingsTests` and `McpToolsTests`.**

- [ ] **Step 3: Shared: enum, ids, defaults**

`HarnessTool.cs`: append `RunRepository, StopRun, RunStatus, SetRunCommand,` after `Report,`.

`HarnessToolIds.For`: add before `_ => string.Empty`:

```csharp
        HarnessTool.RunRepository => "run_repository",
        HarnessTool.StopRun => "stop_run",
        HarnessTool.RunStatus => "run_status",
        HarnessTool.SetRunCommand => "set_run_command",
```

`SettingsDefaults`:
- `Configurable`: append the four after `HarnessTool.Report,`.
- `IsRead`: add `or HarnessTool.RunStatus`.
- `AllowedByDefault`: add `or HarnessTool.RunRepository or HarnessTool.StopRun` to the `tool is` list. `SetRunCommand` stays out, so it asks.
- `Describe`: add
  ```csharp
        HarnessTool.RunRepository => "Run a repository's application",
        HarnessTool.StopRun => "Stop a repository's application",
        HarnessTool.RunStatus => "Read where a repository is served",
        HarnessTool.SetRunCommand => "Change a repository's run command",
  ```

- [ ] **Step 4: Features/Mcp: arguments and specs**

`ToolArguments.cs`: add constants `Command = "command"`, `Port = "port"`, `Path = "path"`.

`McpTools.All`: add before the `Dispatch` spec:

```csharp
        Spec(HarnessTool.RunRepository, "Start a repository's application from its run profile.", Repository),
        Spec(HarnessTool.StopRun, "Stop a repository's running application.", Repository),
        Spec(HarnessTool.RunStatus, "Report whether a repository's application runs and at which URL.", Repository),
        Spec(
            HarnessTool.SetRunCommand,
            "Set how a repository's application starts: shell command, port, optional URL path. Empty command removes it.",
            Repository,
            new ToolParam(ToolArguments.Command, "string", "One shell line, e.g. npm run dev. Empty removes the profile.", true),
            new ToolParam(ToolArguments.Port, "integer", "The TCP port it listens on. Required unless command is empty.", true),
            new ToolParam(ToolArguments.Path, "string", "URL path, default /.", false)),
```

- [ ] **Step 5: Cli: adapter, actions, wiring**

`Adapters.cs`: after `Agents()` add `public static IRunStore Runs() => new JsonRunStore();` (add `using Fleet.Ports.Runs;`; `Fleet.Platform.Storage` is already imported there).

`McpActions` constructor gains `IRunStore runs` after `IAgentStore store`. Add fields:

```csharp
    private readonly RunRepositoryHandler _runner = new(mux, runs, PortProbe.InUse, OperatingSystem.IsWindows());

    private readonly StopRunHandler _runStopper = new(mux);

    private readonly SetRunCommandHandler _runSetter = new(runs);
```

Add usings `Fleet.Features.Repositories;`, `Fleet.Features.Repositories.RunRepository;`, `Fleet.Ports.Runs;`, `Fleet.Ports.Runs.Models;`.

Switch cases (before `HarnessTool.Dispatch`):

```csharp
            HarnessTool.RunRepository => await RunRepository(request, ct).ConfigureAwait(false),
            HarnessTool.StopRun => await StopRun(request, ct).ConfigureAwait(false),
            HarnessTool.RunStatus => await RunStatusOf(request, ct).ConfigureAwait(false),
            HarnessTool.SetRunCommand => await SetRunCommand(request, ct).ConfigureAwait(false),
```

Methods (next to `PullRepository`):

```csharp
    private async Task<McpResult> RunRepository(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return Missing(request, ToolArguments.Repository) ?? NoRepository(request);
        }

        var started = await _runner
            .HandleAsync(project, root, repo.Name, repo.Path, repo.DefaultBranch, ct)
            .ConfigureAwait(false);

        return started.Succeeded
            ? Ok($"{repo.Name} is starting at {started.Value}.")
            : McpResult.Error(started.Error!);
    }

    private async Task<McpResult> StopRun(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return Missing(request, ToolArguments.Repository) ?? NoRepository(request);
        }

        return From(await _runStopper.HandleAsync(repo.Name, ct).ConfigureAwait(false), $"{repo.Name} stopped.");
    }

    private async Task<McpResult> RunStatusOf(McpRequest request, CancellationToken ct)
    {
        var repo = await Resolve(request, ct).ConfigureAwait(false);

        if (repo is null)
        {
            return Missing(request, ToolArguments.Repository) ?? NoRepository(request);
        }

        var profile = runs.List(project).FirstOrDefault(p =>
            string.Equals(p.Repository, repo.Name, StringComparison.OrdinalIgnoreCase));
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);
        var status = RunStatus.For(repo.Name, profile, panes);

        if (profile is null)
        {
            return Ok($"{repo.Name}: no run profile{(status.Running ? ", but a run pane is open" : string.Empty)}.");
        }

        return Ok(status.Running
            ? $"{repo.Name} is running at {status.Url}."
            : $"{repo.Name} is not running; it would serve {status.Url}.");
    }

    private async Task<McpResult> SetRunCommand(McpRequest request, CancellationToken ct)
    {
        if (Missing(request, ToolArguments.Repository) is { } error)
        {
            return error;
        }

        var command = ToolArguments.Text(request, ToolArguments.Command);

        if (command.Length > 0 && Missing(request, ToolArguments.Port) is { } noPort)
        {
            return noPort;
        }

        var summaries = await _repos.HandleAsync(root, ct).ConfigureAwait(false);
        var repo = summaries.FirstOrDefault(s =>
            string.Equals(s.Name, Repo(request), StringComparison.OrdinalIgnoreCase));

        if (repo is null)
        {
            return NoRepository(request);
        }

        var port = ToolArguments.Count(request, ToolArguments.Port, 0);
        var path = ToolArguments.Text(request, ToolArguments.Path);
        var panes = await mux.ListPanesAsync(ct).ConfigureAwait(false);

        var set = _runSetter.Handle(
            project,
            summaries.Select(s => s.Name).ToList(),
            panes,
            new RunProfile(repo.Name, command, port, path));

        return set.Succeeded ? Ok(set.Value) : McpResult.Error(set.Error!);
    }
```

`command` is not required: an empty command means "remove", and then `port` is not required either. Both rules are stated in the tool description so agents do not send dummy ports. The repository list is fetched once and reused for the existence check.

`McpWiring.cs`: pass `Adapters.Runs(),` after `Adapters.Agents(),`.

- [ ] **Step 6: Run the full suite. Expected: green, including `ClaudePermissionPlannerTests.Every_rule_is_a_fleet_tool_or_a_known_git_gate`.**

- [ ] **Step 7: Commit**

```
git add src/Fleet/Shared/Settings src/Fleet/Features/Mcp/ServeMcp src/Fleet/Cli/Composition/McpActions.cs src/Fleet/Cli/Composition/McpWiring.cs src/Fleet/Cli/Composition/Adapters.cs tests/Fleet.Tests/Shared/SettingsTests.cs tests/Fleet.Tests/Features/Mcp/McpToolsTests.cs && git commit -q -m "feat: agents can run, stop, inspect and configure a repository's application"
```

### Task 10: Manage picker entries

**Files:**
- Modify: `src/Fleet/Features/Repositories/RepositoryChores.cs`
- Test: `tests/Fleet.Tests/Features/Repositories/RepositoryChoresTests.cs`

- [ ] **Step 1: Rewrite the failing tests**

Replace the first two tests with:

```csharp
    [Theory]
    [InlineData(false, "run", "Run the application")]
    [InlineData(true, "stop", "Stop the application")]
    public void The_manage_menu_carries_run_or_stop_by_state_plus_the_command_editor(
        bool running, string runLabel, string runDetail)
    {
        var entries = RepositoryChores.Entries(running);

        Assert.Equal(7, entries.Count);
        Assert.Equal(
            ["branch", "pull", "remove", "secrets", "rename", runLabel, "command"],
            entries.Select(e => e.Label));
        Assert.Equal(RepositoryChores.Choices(running), entries.Select(e => e.Detail));
        Assert.Equal(runDetail, entries[RepositoryChores.Run].Detail);
        Assert.Equal("Edit the run command", entries[RepositoryChores.SetRun].Detail);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Its_keys_read_as_mnemonics_and_do_not_shift_when_the_run_state_flips(bool running)
    {
        var keys = PickerKeys.For(RepositoryChores.Entries(running));

        Assert.Equal(["b", "p", "r", "s", "e", "u", "c"], keys);
    }
```

- [ ] **Step 2: Run, expect compile failure** (`Entries` is a property today).

- [ ] **Step 3: Implement**

Replace `RepositoryChores.cs` with:

```csharp
using Fleet.Ui.Models;

namespace Fleet.Features.Repositories;

public static class RepositoryChores
{
    public const int DefaultBranch = 0;

    public const int Pull = 1;

    public const int Remove = 2;

    public const int Secrets = 3;

    public const int Rename = 4;

    public const int Run = 5;

    public const int SetRun = 6;

    public static IReadOnlyList<string> Choices(bool running) =>
    [
        "Change the default branch",
        "Pull the default branch to latest",
        "Remove the repository from this project",
        "Files every worktree needs but git does not carry",
        "Rename the repository folder",
        running ? "Stop the application" : "Run the application",
        "Edit the run command",
    ];

    public static IReadOnlyList<PickerEntry> Entries(bool running)
    {
        var choices = Choices(running);

        return
        [
            new("branch", choices[DefaultBranch]),
            new("pull", choices[Pull]),
            new("remove", choices[Remove]),
            new("secrets", choices[Secrets]),
            new("rename", choices[Rename]),
            new(running ? "stop" : "run", choices[Run], Key: "u"),
            new("command", choices[SetRun], Key: "c"),
        ];
    }
}
```

Three other call sites use `Entries` as a property and must change in the same commit:

- `src/Fleet/Cli/Composition/DashboardWiring.cs`: `FleetPicker.Choose(app, repository.Name, RepositoryChores.Entries, keymap)` → `RepositoryChores.Entries(false)` for now; Task 12 passes the real state.
- `tests/Fleet.Tests/Features/Repositories/RenameRepositoryTests.cs` (`The_manage_menu_offers_rename`): both `RepositoryChores.Entries` → `RepositoryChores.Entries(false)`.
- `tests/Fleet.Tests/Features/Repositories/SecretsTests.cs` (`The_manage_menu_offers_secrets_with_its_own_key`): `Entries` → `Entries(false)` and the expected keys become `["b", "p", "r", "s", "e", "u", "c"]`.

- [ ] **Step 4: Run the full suite. Expected: green.**

- [ ] **Step 5: Commit**

```
git add src/Fleet/Features/Repositories/RepositoryChores.cs src/Fleet/Cli/Composition/DashboardWiring.cs tests/Fleet.Tests/Features/Repositories/RepositoryChoresTests.cs tests/Fleet.Tests/Features/Repositories/RenameRepositoryTests.cs tests/Fleet.Tests/Features/Repositories/SecretsTests.cs && git commit -q -m "feat: manage picker offers run/stop and the run command editor"
```

### Task 11: Row pill through the callbacks

**Files:**
- Modify: `src/Fleet/Features/Dashboard/ShowDashboard/DashboardRows.cs`
- Modify: `src/Fleet/Features/Dashboard/ShowDashboard/Models/DashboardCallbacks.cs` (add `Func<RepositoryChoice, string?> RepositoryRunPill,` after `RepositoryState`)
- Modify: `src/Fleet/Features/Dashboard/ShowDashboard/ShowDashboardView.cs` (`RefreshAsync` passes `callbacks.RepositoryRunPill`)
- Modify: `src/Fleet/Cli/Composition/DashboardWiring.cs` (temporary `RepositoryRunPill: _ => null` so it compiles; Task 12 fills it)
- Test: `tests/Fleet.Tests/Features/Dashboard/DashboardRowsTests.cs`

- [ ] **Step 1: Add failing tests and update the signature in existing ones**

Every existing `DashboardRows.ForRepositories(x, state)` call in the test file gains a third argument `_ => null`. Add:

```csharp
    [Fact]
    public void A_running_repository_shows_its_port_pill_between_branch_and_name()
    {
        var rows = DashboardRows.ForRepositories(
            [Repo("frontend")], _ => BranchState.Unknown, r => r.Name == "frontend" ? ":5173" : null);

        var text = rows[0].Text;
        Assert.True(text.IndexOf("develop", StringComparison.Ordinal) < text.IndexOf(":5173", StringComparison.Ordinal));
        Assert.True(text.IndexOf(":5173", StringComparison.Ordinal) < text.IndexOf("frontend", StringComparison.Ordinal));
    }

    [Fact]
    public void A_repository_that_is_not_running_shows_no_run_pill()
    {
        var rows = DashboardRows.ForRepositories([Repo("frontend")], _ => BranchState.Unknown, _ => null);

        Assert.DoesNotContain(":", rows[0].Text);
    }
```

- [ ] **Step 2: Run, expect compile failure.**

- [ ] **Step 3: Implement**

`ForRepositories` becomes:

```csharp
    public static IReadOnlyList<FleetRow> ForRepositories(
        IReadOnlyList<RepositoryChoice> repositories,
        Func<RepositoryChoice, BranchState> state,
        Func<RepositoryChoice, string?> runPill)
    {
        if (repositories.Count == 0)
        {
            return [FleetRow.Plain(EmptyHint)];
        }

        var pills = repositories
            .Select(r => BranchStatus.Pill(r.DefaultBranch, state(r)))
            .ToList();

        var pillWidth = pills.Max(p => p.Sum(s => s.Text.Length));

        return
        [
            .. repositories.Select((r, i) => new FleetRow(
            [
                .. pills[i],
                FleetSpan.Plain(new string(' ', pillWidth - pills[i].Sum(s => s.Text.Length) + 3)),
                .. RunSpans(runPill(r)),
                FleetSpan.Muted(r.Name),
            ])),
        ];
    }

    private static IEnumerable<FleetSpan> RunSpans(string? pill) =>
        pill is null ? [] : [FleetSpan.Muted(pill), FleetSpan.Plain("  ")];
```

`DashboardCallbacks`: add `Func<RepositoryChoice, string?> RepositoryRunPill,` right after `Func<RepositoryChoice, BranchState> RepositoryState,`.

`ShowDashboardView.RefreshAsync`: `DashboardRows.ForRepositories(loaded, callbacks.RepositoryState, callbacks.RepositoryRunPill)`.

`DashboardWiring`: after the `RepositoryState:` entry add `RepositoryRunPill: _ => null,` (replaced in Task 12).

- [ ] **Step 4: Run the full suite. Expected: green.**

- [ ] **Step 5: Commit**

```
git add src/Fleet/Features/Dashboard tests/Fleet.Tests/Features/Dashboard/DashboardRowsTests.cs src/Fleet/Cli/Composition/DashboardWiring.cs && git commit -q -m "feat: repository rows can carry a run pill supplied by the composition root"
```

### Task 12: Dashboard wiring, prompt, guards

**Files:**
- Create: `src/Fleet/Features/Repositories/RunRepository/RunPrompt.cs`
- Modify: `src/Fleet/Cli/Composition/DashboardWiring.cs`
- Modify: `src/Fleet/Cli/Commands/DashCommand.cs`, `src/Fleet/Cli/Commands/MenuCommand.cs` (pass `Adapters.Runs()`)
- Modify: `src/Fleet/Cli/Composition/McpActions.cs` (remove path: refuse while running, delete profile)

No unit tests cover `Cli/Composition` (it is composition). `UiThreadTests` and `SliceBoundaryTests` still apply: every modal opened after an `await` must go through `FleetAsync.OnUi`. `UiThreadTests` only knows `FleetPrompt.*`, `*View.Show`, `FleetPicker`, `FleetDialog`; `RunPrompt.Show` would slip past it, so Step 0 widens the pattern first.

- [ ] **Step 0: Teach UiThreadTests about `*Prompt.Show`**

In `tests/Fleet.Tests/Architecture/UiThreadTests.cs` change the `UiCall` regex alternation `FleetPrompt\.\w+` to `\w+Prompt\.\w+`. Run the suite: still green (nothing calls a `*Prompt` after an await yet). Commit: `git add tests/Fleet.Tests/Architecture/UiThreadTests.cs && git commit -q -m "test: any *Prompt.Show after an await must run on the UI thread"`.

- [ ] **Step 1: RunPrompt**

`src/Fleet/Features/Repositories/RunRepository/RunPrompt.cs` — three sequential text prompts, built on `FleetPrompt.Text` (a full row form like `AddRepositoryView` is more than three fields need):

```csharp
using Fleet.Ports.Runs.Models;
using Fleet.Ui;
using Terminal.Gui.App;

namespace Fleet.Features.Repositories.RunRepository;

public static class RunPrompt
{
    public static RunProfile? Show(IApplication app, string repository, RunProfile? current)
    {
        var command = FleetPrompt.Text(
            app, $"Run {repository}", current?.Command ?? string.Empty, "Command (empty removes)", allowEmpty: true);

        if (command is null)
        {
            return null;
        }

        if (command.Length == 0)
        {
            return new RunProfile(repository, string.Empty, current?.Port ?? 0, RunProfile.DefaultPath);
        }

        var port = FleetPrompt.Text(
            app, $"Run {repository}", current?.Port.ToString() ?? string.Empty, "Port");

        if (port is null)
        {
            return null;
        }

        var path = FleetPrompt.Text(
            app, $"Run {repository}", current?.Path ?? RunProfile.DefaultPath, "URL path", allowEmpty: true);

        if (path is null)
        {
            return null;
        }

        return new RunProfile(
            repository, command, int.TryParse(port, out var parsed) ? parsed : 0, path);
    }
}
```

A non-numeric port becomes 0 and `SetRunCommandHandler` rejects it with "port 0 is outside 1-65535".

- [ ] **Step 2: Wiring**

In `DashboardWiring.For`:

1. Signature: add `IRunStore runs,` after `IAgentStore agents,`. Update both callers (`DashCommand.cs`, `MenuCommand.cs`) to pass `Adapters.Runs(),` after `Adapters.Agents(),`. Add usings `Fleet.Features.Repositories.RunRepository;`, `Fleet.Ports.Runs;`, `Fleet.Ports.Runs.Models;`.

2. Handlers, next to `repoRenamer`:

```csharp
        var runner = new RunRepositoryHandler(mux, runs, PortProbe.InUse, OperatingSystem.IsWindows());
        var runStopper = new StopRunHandler(mux);
        var runSetter = new SetRunCommandHandler(runs);

        RunProfile? ProfileOf(string repository) => runs.List(project.Name)
            .FirstOrDefault(p => string.Equals(p.Repository, repository, StringComparison.OrdinalIgnoreCase));

        bool IsRunning(string repository, IReadOnlyList<Pane> panes) =>
            RunStatus.For(repository, ProfileOf(repository), panes).Running;
```

3. `RepositoryRunPill`: replace the placeholder with

```csharp
            RepositoryRunPill: repository =>
            {
                var profile = ProfileOf(repository.Name);
                var status = RunStatus.For(repository.Name, profile, barPanes ?? []);

                return !status.Running ? null : profile is null ? "run" : $":{profile.Port}";
            },
```

`barPanes` is the pane list `WithBarState` refreshes for `LoadAgents`/`LoadSubs`. `RefreshAsync` builds the repository rows before it calls `LoadAgents`, so the pill reads the previous cycle's panes: null on the very first paint, and one refresh behind a start or stop. The dashboard refreshes every heartbeat, so this is a blink, not a bug; accepted.

4. `ManageRepository`: the picker now needs the state:

```csharp
                var panes = await mux.ListPanesAsync().ConfigureAwait(false);
                var running = IsRunning(repository.Name, panes);

                var picked = await FleetAsync
                    .OnUi(app, () => FleetPicker.Choose(
                        app, repository.Name, RepositoryChores.Entries(running), keymap))
                    .ConfigureAwait(false);
```

Then add, before the `if (picked != RepositoryChores.DefaultBranch)` line:

```csharp
                if (picked == RepositoryChores.Run && running)
                {
                    var stopped = await runStopper.HandleAsync(repository.Name).ConfigureAwait(false);

                    return new RepositoryManaged(Noted(log, project.Name, stopped.Succeeded
                        ? $"{repository.Name} stopped."
                        : stopped.Error));
                }

                if (picked == RepositoryChores.Run)
                {
                    if (ProfileOf(repository.Name) is null)
                    {
                        var wanted = await FleetAsync
                            .OnUi(app, () => RunPrompt.Show(app, repository.Name, null))
                            .ConfigureAwait(false);

                        if (wanted is null)
                        {
                            return RepositoryManaged.Nothing;
                        }

                        var set = runSetter.Handle(project.Name, RepositoryNames(), panes, wanted);

                        if (!set.Succeeded)
                        {
                            return new RepositoryManaged(Noted(log, project.Name, set.Error));
                        }
                    }

                    var started = await runner
                        .HandleAsync(project.Name, project.Root, repository.Name, repository.Directory, repository.DefaultBranch)
                        .ConfigureAwait(false);

                    return new RepositoryManaged(Noted(log, project.Name, started.Succeeded
                        ? $"{repository.Name} is starting at {started.Value}."
                        : started.Error));
                }

                if (picked == RepositoryChores.SetRun)
                {
                    var wanted = await FleetAsync
                        .OnUi(app, () => RunPrompt.Show(app, repository.Name, ProfileOf(repository.Name)))
                        .ConfigureAwait(false);

                    if (wanted is null)
                    {
                        return RepositoryManaged.Nothing;
                    }

                    var set = runSetter.Handle(project.Name, RepositoryNames(), panes, wanted);

                    return new RepositoryManaged(Noted(log, project.Name, set.Succeeded ? set.Value : set.Error));
                }
```

with a local helper next to `ProfileOf`:

```csharp
        async Task<List<string>> RepositoryNamesAsync() =>
            (await repositories.HandleAsync(project.Root).ConfigureAwait(false)).Select(r => r.Name).ToList();
```

and both `runSetter.Handle(project.Name, RepositoryNames(), panes, wanted)` calls written as
`runSetter.Handle(project.Name, await RepositoryNamesAsync().ConfigureAwait(false), panes, wanted)`.

5. Guards. In the `Rename` branch (inside `ManageRepository`, where `running` is already computed), before the `owned` check:

```csharp
                    if (running)
                    {
                        return new RepositoryManaged($"{repository.Name} is running; stop it first.");
                    }
```

In `RemoveRepository`, before the `owned` check:

```csharp
                if (IsRunning(repository.Name, await mux.ListPanesAsync().ConfigureAwait(false)))
                {
                    return $"{repository.Name} is running; stop it first.";
                }
``` After a successful remove: `runs.Remove(project.Name, repository.Name);`. After a successful rename: if `ProfileOf(repository.Name)` is `{ } old`, `runs.Remove(project.Name, repository.Name); runs.Save(project.Name, old with { Repository = name.Trim() });`.

6. `McpActions.RemoveRepository`: before `_repoRemover.Handle`, list panes and refuse with `McpResult.Error($"{repo.Name} is running; stop it first.")` when `RunPanes.Owns` matches; after success call `runs.Remove(project, repo.Name)`.

- [ ] **Step 3: Build and run the full suite. Expected: green; `UiThreadTests` in particular.**

- [ ] **Step 4: Commit**

```
git add src/Fleet/Features/Repositories/RunRepository/RunPrompt.cs src/Fleet/Cli && git commit -q -m "feat: run, stop and configure a repository from the Repositories tab"
```

### Task 13: Publish, install, smoke test, notes

- [ ] **Step 1: Publish and install**

Run in PowerShell: `& C:\repos\fleet\install.ps1` (adds vswhere to PATH itself). Expected tail: `installed C:\Users\...\Programs\fleet\fleet.exe`.

- [ ] **Step 2: Smoke test on a scratch project, never techweb**

Open a scratch project's dash (see memory note "Scratch project for side effects"). On the Repositories tab press `m` on a repo: pick "run", enter `cmd /c "timeout /t 60"` style command (Windows) with port 5199, path `/`. Expect: a tab `<repo> run` appears in the project window, the row shows `:5199`, `m` now offers "stop". Press `m`, "stop": tab closes, pill gone. Press Enter on the repo while running: nvim opens, the run tab is untouched.

- [ ] **Step 3: Verify the "verify before coding" items and record them**

From an agent pane, ask its Claude to call `run_status` to confirm the tools are listed. Then append a "Verified" section to the spec noting: the installed binary refused a bound port (start the run twice), quotes survive `cmd /c` (use `python -c "print(\"x\"); import time; time.sleep(60)"` as the command), and killing the pane ended the child (the `python` process is gone from Task Manager after "stop"). Commit the spec update.

- [ ] **Step 4: Final commit if anything changed**

```
git add docs && git commit -q -m "docs: run-repository spec, verified items recorded"
```
