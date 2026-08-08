# fleet Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Running `fleet` shows a project picker; choosing a project opens a terminal window split into a Claude pane and a fleet dashboard pane, from which repositories can be created or cloned as bare repos with a default-branch worktree.

**Architecture:** Vertical slices. One folder per behaviour, containing everything needed to read or change it. All I/O sits behind four ports implemented in `Platform/`; `Program.cs` is the only file permitted to name a `Platform` type. Slice isolation is enforced by architecture tests, not by discipline.

**Tech Stack:** C# / .NET 10, NativeAOT, Terminal.Gui 2.4.17, System.Text.Json source generators, xUnit. No reflection-based frameworks — see *Rejected patterns*.

**Spec:** `docs/phase1.md`. **Design:** `docs/DESIGN.md`.

---

## Architecture

### Adopted patterns, and why each one earns its place

These are chosen for a codebase that will be read and edited largely by an LLM. The common thread is **context locality** and a **visible call graph**: a change should require reading one folder, and every call should be traceable by reading source rather than by guessing what a framework does at runtime.

**1. Vertical slices.** One folder per behaviour — `Features/Repositories/AddRepository/` — holding its command, handler, and UI. The single biggest lever: a change means reading one folder, not tracing a request through four layers.

**2. One behaviour file per slice.** Request record, handler, and validation live together in one 60–150 line file. It fits in one read, and an edit does not span files. UI is a sibling file because dialogs are bulky.

**3. Explicit composition root.** `Program.cs` constructs every adapter and hands dependencies in via constructors. No service locator, no static mutable state, no ambient context. Any file can be understood in isolation because its dependencies are its constructor parameters.

**4. Ports and adapters, deliberately few.** Four interfaces cover all I/O: `IMuxDriver`, `IGitRunner`, `IProjectStore`, `IFleetLog`. A slice's dependency list is short and honest, and every one of them has a fake.

**5. `Result<T>` for expected failures.** Failure appears in the signature instead of hiding at some throw site. Exceptions are reserved for defects. This pairs directly with the fail-silent invariant from `DESIGN.md`: expected degradation is a value, a bug is an exception.

**6. Sealed records, no inheritance.** A flat reading graph. No virtual dispatch to chase, no base class holding behaviour that the file in front of you does not show.

**7. Mirrored test paths.** A slice's tests are at a mechanically predictable path: `tests/Fleet.Tests/Features/Repositories/AddRepository/`. Colocating tests inside `src/` would fight the AOT publish, so mirroring is the compromise.

**8. Architecture tests over source text.** Three rules — no cross-slice references, `Features/` may not touch `Platform/`, only `Program.cs` may — checked by scanning `.cs` files. Source scanning rather than reflection because it catches references inside method bodies, which signature reflection misses, and because it needs no fragile IL parsing.

**9. Naming is documentation.** The folder is the verb. `AddRepository`, `OpenProject`, `RunDoctor`.

### Rejected patterns

Each of these is common and each would make things worse here.

**MediatR, or any reflection-based dispatch.** Two independent reasons. It would break NativeAOT — handler resolution is reflection, which surfaces as `IL2026`/`IL3050` and fails the build under `IsAotCompatible`. And it hides the call graph behind runtime resolution, which is precisely what makes a codebase hard to follow. Handlers are called directly, by name.

**Repository pattern over git.** Git *is* the store. A wrapper around `IGitRunner` that adds no behaviour is a layer to read past.

**DDD aggregates and domain events.** No invariant in phase 1 needs a consistency boundary. Adding one buys ceremony.

**Layer projects — Core / Application / Infrastructure / UI.** Directly opposed to vertical slicing: adding one behaviour would touch four projects. This is the layout `DESIGN.md` originally proposed, and slicing replaces it.

**AutoMapper and convention-based magic.** Reflection, AOT-hostile, and behaviour you cannot see by reading.

### Two projects, not six

```
fleet.slnx         → the .NET 10 SDK creates the new XML solution format
src/Fleet          → the binary (AOT-published as `fleet`)
tests/Fleet.Tests  → everything, mirroring the slice tree
```

**Honest tradeoff:** project references enforce dependency direction at compile time; namespaces do not. Collapsing to one source project trades that compile-time guarantee for a test-time one. It is worth it because six projects for a phase-1 TUI is ceremony, and because a boundary violation caught by `dotnet test` is caught before it ever lands. If the architecture tests prove insufficient, splitting `Platform` into its own project later is mechanical.

### Layout

```
src/Fleet/
  Program.cs                          composition root — the ONLY file naming a Platform type
  Fleet.csproj

  Shared/                             pure, no I/O, referenced by anything
    Result.cs
    ProjectName.cs                    sanitize a name into a filename
    HomePath.cs                       contract/expand ~

  Ports/                              interfaces and the types that cross them
    IFleetLog.cs
    Git/IGitRunner.cs                 + GitResult
    Projects/IProjectStore.cs         + Project
    Mux/IMuxDriver.cs                 + PaneId, Pane, SpawnOptions, SplitOptions, MuxCaps,
                                        MuxUnavailableException

  Platform/                           every implementation that touches the outside world
    Logging/FileLog.cs
    Storage/FleetPaths.cs
    Storage/FleetJsonContext.cs
    Storage/JsonProjectStore.cs
    Git/GitRunner.cs
    Mux/DriverSelector.cs             + MuxEnvironment
    Mux/FailSilentDriver.cs           the fail-silent invariant, enforced once
    Mux/Fake/FakeMuxDriver.cs
    Mux/WezTerm/CwdUrl.cs
    Mux/WezTerm/WezTermCli.cs
    Mux/WezTerm/WezTermPaneJson.cs
    Mux/WezTerm/WezTermDriver.cs

  Features/
    Projects/
      PickProject/PickProject.cs      behaviour
      PickProject/PickProjectView.cs  UI
      CreateProject/CreateProject.cs
      CreateProject/CreateProjectView.cs
      OpenProject/OpenProject.cs      spawn window, split, focus
    Repositories/
      BranchSlug.cs                   area-shared: used by more than one slice here
      AddRepository/AddRepository.cs
      AddRepository/AddRepositoryView.cs
      ListRepositories/ListRepositories.cs
    Dashboard/
      ShowDashboard/ShowDashboard.cs
      ShowDashboard/ShowDashboardView.cs
    Diagnostics/
      RunDoctor/RunDoctor.cs

tests/Fleet.Tests/
  Architecture/SliceBoundaryTests.cs
  Shared/ResultTests.cs
  Shared/ProjectNameTests.cs
  Shared/HomePathTests.cs
  Platform/Storage/JsonProjectStoreTests.cs
  Platform/Mux/DriverSelectorTests.cs
  Platform/Mux/FailSilentDriverTests.cs
  Platform/Mux/Fake/FakeMuxDriverTests.cs
  Platform/Mux/WezTerm/CwdUrlTests.cs
  Features/Projects/OpenProject/OpenProjectTests.cs
  Features/Repositories/BranchSlugTests.cs
  Features/Repositories/AddRepository/AddRepositoryTests.cs
  Features/Repositories/ListRepositories/ListRepositoriesTests.cs
```

**Sharing rule:** sharing *within* an area is allowed and goes in the area root (`Features/Repositories/BranchSlug.cs`). Sharing *across* areas goes to `Shared/` and must be pure. A slice never references another slice.

---

## Deltas from DESIGN.md

`phase1.md` narrows and adds; this plan also changes the layout. Recorded so the documents stay honest.

| Item | DESIGN.md said | Now |
|---|---|---|
| Layout | Six layer projects (`Fleet.Core`, `Fleet.Mux`, …) | **Vertical slices in two projects.** `DESIGN.md` layout section to be updated |
| Repo layouts | Three, detected not configured | **Bare container only.** The other two deferred |
| Default branch | Derived: `origin/HEAD` → `HEAD` → `main` | **Prompted** at add time. Derivation becomes the pre-filled default |
| Dashboard | A pane you attach to | **Two panes side by side.** `SplitAsync` is required in phase 1 |
| Entry point | `fleet up <project>` | Bare `fleet` opens a **picker** |
| Agents | `new`, `send`, `reap` in MVP | **Not in phase 1.** The agent pane renders a placeholder |
| Error handling | Fail-silent by exception swallowing | **`Result<T>` for expected failures**, plus the `FailSilentDriver` decorator at the mux boundary |

Two decisions taken during planning:

- **"Add an existing repository" means clone a remote URL as bare.** Adopting a local directory is deferred.
- **The default-branch worktree is created immediately**, so a repo is usable the moment it is added.

That creates one trap: `git worktree add` fails on a freshly `init --bare` repository, because there is no HEAD commit to branch from. Task 15 solves it with plumbing rather than requiring a git version that has `worktree add --orphan`.

---

## Chunk 1: Skeleton and guardrails

### Task 1: Solution, AOT analyzers, CI

**Files:**
- Create: `fleet.sln`, `Directory.Build.props`, `.gitignore`, `.github/workflows/ci.yml`, `src/Fleet/Fleet.csproj`, `tests/Fleet.Tests/Fleet.Tests.csproj`

- [ ] **Step 1: Initialize the repository**

```powershell
cd C:\repos\fleet
git init
git add docs
git commit -m "docs: design and phase 1 plan"
```

- [ ] **Step 2: Create the two projects**

```powershell
dotnet new sln -n fleet
dotnet new console -o src/Fleet -n Fleet
dotnet new xunit -o tests/Fleet.Tests -n Fleet.Tests
dotnet sln add src/Fleet/Fleet.csproj tests/Fleet.Tests/Fleet.Tests.csproj
dotnet add tests/Fleet.Tests reference src/Fleet
```

- [ ] **Step 3: Write `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>
</Project>
```

**The AOT analyzers are deliberately not here.** They belong to `src/Fleet` alone. The test project references xunit, which is reflection-based by design, so enabling the analyzers repository-wide would combine with `TreatWarningsAsErrors` to fail the test build on warnings that are both correct and irrelevant. There is only one source project, so there is nothing to duplicate.

- [ ] **Step 4: Configure the app project**

`src/Fleet/Fleet.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>fleet</AssemblyName>
    <RootNamespace>Fleet</RootNamespace>
  </PropertyGroup>

  <PropertyGroup>
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>
    <IsAotCompatible>true</IsAotCompatible>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
    <EnableSingleFileAnalyzer>true</EnableSingleFileAnalyzer>
  </PropertyGroup>

</Project>
```

`IsAotCompatible` promotes trim and AOT warnings to errors. That is what stops a reflection-based dependency from being discovered at publish time instead of at build time — and it is the mechanism that enforces the *Rejected patterns* list rather than leaving it as advice.

Also add `.gitattributes`, so the workflow's bash steps get LF on every platform:

```
* text=auto
*.sh    text eol=lf
*.yml   text eol=lf
*.yaml  text eol=lf
```

And delete the template's `tests/Fleet.Tests/UnitTest1.cs`.

- [ ] **Step 5: Expose the repository root to the test assembly**

The architecture tests scan source files, so they need to find them. `tests/Fleet.Tests/Fleet.Tests.csproj`:

```xml
  <ItemGroup>
    <AssemblyMetadata Include="RepoRoot" Value="$(MSBuildThisFileDirectory)../../" />
  </ItemGroup>
```

- [ ] **Step 6: Verify it builds, and that AOT publishes**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

Then establish the AOT baseline now, while the project is trivial and a failure
can only be the toolchain:

Run: `dotnet publish src/Fleet -c Release -o out`
Expected: `Fleet -> .../out/`, and `out/fleet.exe --help` prints usage.

**Windows prerequisite.** NativeAOT needs the MSVC linker and the Windows SDK.
Visual Studio Build Tools 2022 with the "Desktop development with C++" workload
supplies both. If the linker is installed but publish still fails with
`'vswhere.exe' is not recognized` followed by `MSB3073 ... exited with code 123`,
the toolchain is fine and only `vswhere` is missing from `PATH`:

```powershell
$env:PATH = "C:\Program Files (x86)\Microsoft Visual Studio\Installer;$env:PATH"
```

Publishing from a Developer PowerShell prompt has the same effect. GitHub's
`windows-latest` runner already has `vswhere` on `PATH`, so CI is unaffected.

Observed baseline on 2026-08-08: `net10.0`, `win-x64`, 1.0 MB `fleet.exe`. Worth
comparing against once Terminal.Gui is added in Task 13.

- [ ] **Step 7: Write the CI workflow**

`.github/workflows/ci.yml`:

```yaml
name: ci
on: [push, pull_request]

jobs:
  build:
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet build --configuration Release
      - run: dotnet test --configuration Release --no-build

  aot-smoke:
    strategy:
      fail-fast: false
      matrix:
        os: [windows-latest, ubuntu-latest]
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.x' }
      - run: dotnet publish src/Fleet -c Release -o out
      - name: Smoke test
        shell: bash
        run: |
          BIN=out/fleet; [ -f out/fleet.exe ] && BIN=out/fleet.exe
          "$BIN" --help
          "$BIN" doctor || echo "doctor reported problems (expected on a headless runner)"
```

`doctor` returns non-zero when no multiplexer is reachable, which is correct locally and expected on a headless runner — so the smoke test asserts the binary *starts and runs*, not that the environment is healthy.

- [ ] **Step 8: Commit**

```powershell
git add .
git commit -m "chore: two-project skeleton, AOT analyzers as errors, CI matrix"
```

---

### Task 2: `Result<T>`

Expected failure becomes a value in the signature. Exceptions stay for defects.

**Files:**
- Create: `src/Fleet/Shared/Result.cs`
- Test: `tests/Fleet.Tests/Shared/ResultTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Shared;

public class ResultTests
{
    [Fact]
    public void Ok_carries_a_value_and_no_error()
    {
        var r = Result<int>.Ok(42);
        Assert.True(r.Succeeded);
        Assert.Equal(42, r.Value);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Fail_carries_an_error_and_throws_on_Value()
    {
        var r = Result<int>.Fail("nope");
        Assert.False(r.Succeeded);
        Assert.Equal("nope", r.Error);
        Assert.Throws<InvalidOperationException>(() => r.Value);
    }

    [Fact]
    public void Fail_rejects_an_empty_reason()
        => Assert.Throws<ArgumentException>(() => Result<int>.Fail("  "));

    [Fact]
    public void Unit_result_works_without_a_payload()
    {
        Assert.True(Result.Ok().Succeeded);
        Assert.Equal("bad", Result.Fail("bad").Error);
    }
}
```

Reading `Value` on a failure throwing is deliberate: it is a defect to ignore `Succeeded`, and a silent default would hide it.

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter ResultTests`
Expected: FAIL — `Result` not found

- [ ] **Step 3: Implement**

```csharp
namespace Fleet.Shared;

/// <summary>
/// An operation that can fail in an expected way. Failure is part of the
/// signature rather than an exception thrown from somewhere unseen; exceptions
/// are reserved for defects.
/// </summary>
public readonly struct Result<T>
{
    private readonly T _value;

    private Result(bool succeeded, T value, string? error)
    {
        Succeeded = succeeded;
        _value = value;
        Error = error;
    }

    public bool Succeeded { get; }
    public string? Error { get; }

    /// <summary>Throws when the result failed: reading a value you did not check is a defect.</summary>
    public T Value => Succeeded
        ? _value
        : throw new InvalidOperationException($"Result failed: {Error}");

    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Fail(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("A failure needs a reason", nameof(error));
        return new Result<T>(false, default!, error);
    }
}

/// <summary>A result with no payload.</summary>
public readonly struct Result
{
    private Result(bool succeeded, string? error)
    {
        Succeeded = succeeded;
        Error = error;
    }

    public bool Succeeded { get; }
    public string? Error { get; }

    public static Result Ok() => new(true, null);

    public static Result Fail(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
            throw new ArgumentException("A failure needs a reason", nameof(error));
        return new Result(false, error);
    }
}
```

- [ ] **Step 4: Run to confirm it passes**

Run: `dotnet test --filter ResultTests`
Expected: PASS, 4 tests

- [ ] **Step 5: Commit**

```powershell
git add src/Fleet/Shared/Result.cs tests/Fleet.Tests/Shared/ResultTests.cs
git commit -m "feat: Result type for expected failures"
```

---

### Task 3: Architecture tests

Written **before** any slice exists, so the guardrail is in place from the first one. Source-text scanning rather than reflection: it catches references inside method bodies, which signature reflection misses, and needs no IL parsing.

**Files:**
- Create: `tests/Fleet.Tests/Architecture/SliceBoundaryTests.cs`

- [ ] **Step 1: Write the test**

It passes trivially now — there are no slices to violate anything. That is fine; it starts failing the moment someone crosses a boundary.

```csharp
using System.Reflection;

namespace Fleet.Tests.Architecture;

/// <summary>
/// The layout rules from the plan, enforced mechanically. Scans source text so a
/// reference inside a method body is caught, not just one in a signature.
/// </summary>
public class SliceBoundaryTests
{
    private static string RepoRoot { get; } =
        typeof(SliceBoundaryTests).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(a => a.Key == "RepoRoot").Value
        ?? throw new InvalidOperationException("RepoRoot assembly metadata is missing");

    private static string FeaturesDir => Path.Combine(RepoRoot, "src", "Fleet", "Features");
    private static string SrcDir => Path.Combine(RepoRoot, "src", "Fleet");

    private static IEnumerable<string> CsFiles(string dir) =>
        Directory.Exists(dir) ? Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories) : [];

    /// <summary>Features/&lt;Area&gt;/&lt;Slice&gt; — returns null for files above slice level.</summary>
    private static (string Area, string Slice)? SliceOf(string file)
    {
        var rel = Path.GetRelativePath(FeaturesDir, file).Replace('\\', '/');
        var parts = rel.Split('/');
        return parts.Length >= 3 ? (parts[0], parts[1]) : null;
    }

    [Fact]
    public void No_slice_references_another_slice()
    {
        var violations = new List<string>();

        foreach (var file in CsFiles(FeaturesDir))
        {
            var own = SliceOf(file);
            if (own is null) continue;

            var text = File.ReadAllText(file);

            foreach (var other in CsFiles(FeaturesDir).Select(SliceOf).OfType<(string Area, string Slice)>().Distinct())
            {
                if (other == own.Value) continue;
                var ns = $"Fleet.Features.{other.Area}.{other.Slice}";
                if (text.Contains(ns, StringComparison.Ordinal))
                    violations.Add($"{Path.GetRelativePath(RepoRoot, file)} references {ns}");
            }
        }

        Assert.Empty(violations);
    }

    [Fact]
    public void Features_never_reference_Platform()
    {
        var violations = CsFiles(FeaturesDir)
            .Where(f => File.ReadAllText(f).Contains("Fleet.Platform", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Only_Program_references_Platform_implementations()
    {
        var allowed = Path.Combine(SrcDir, "Program.cs");

        var violations = CsFiles(SrcDir)
            .Where(f => !f.StartsWith(Path.Combine(SrcDir, "Platform"), StringComparison.OrdinalIgnoreCase))
            .Where(f => !string.Equals(f, allowed, StringComparison.OrdinalIgnoreCase))
            .Where(f => File.ReadAllText(f).Contains("Fleet.Platform", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();

        Assert.Empty(violations);
    }

    [Fact]
    public void Ports_depend_on_nothing_but_Shared()
    {
        var portsDir = Path.Combine(SrcDir, "Ports");

        var violations = CsFiles(portsDir)
            .Where(f =>
            {
                var t = File.ReadAllText(f);
                return t.Contains("Fleet.Platform", StringComparison.Ordinal)
                    || t.Contains("Fleet.Features", StringComparison.Ordinal);
            })
            .Select(f => Path.GetRelativePath(RepoRoot, f))
            .ToList();

        Assert.Empty(violations);
    }
}
```

Known limitation, stated rather than hidden: a violation written without naming the namespace — same-namespace types, or a `global using` — slips past. If that ever happens in practice, add `NetArchTest.Rules` alongside these rather than replacing them.

Two rules beyond the four listed above are worth having, and were added during implementation:

- `Shared_depends_on_nothing_inside_Fleet` — `Shared/` is pure, so it may not name `Ports`, `Platform`, or `Features`.
- `The_source_tree_was_actually_found` — asserts `RepoRoot` resolves to a directory containing `.cs` files. Without it, a broken `RepoRoot` would make every other rule pass vacuously, which is the one failure mode of a source-scanning test.

- [ ] **Step 2: Run it**

Run: `dotnet test --filter SliceBoundaryTests`
Expected: PASS, 6 tests

- [ ] **Step 2b: Prove the rules actually bite**

Six passing tests mean nothing yet — there are no slices to violate anything. Plant deliberate violations, confirm they are caught, then remove them.

```powershell
New-Item -ItemType Directory -Force -Path `
  src/Fleet/Platform/Probe, src/Fleet/Features/Probe/AlphaSlice, src/Fleet/Features/Probe/BetaSlice | Out-Null

@'
namespace Fleet.Platform.Probe;
internal static class ProbeThing { public const int N = 1; }
'@ | Set-Content src/Fleet/Platform/Probe/ProbeThing.cs

@'
namespace Fleet.Features.Probe.AlphaSlice;
using Fleet.Platform.Probe;
internal static class Alpha { public static int Use() => ProbeThing.N; }
'@ | Set-Content src/Fleet/Features/Probe/AlphaSlice/Alpha.cs

@'
namespace Fleet.Features.Probe.BetaSlice;
using Fleet.Features.Probe.AlphaSlice;
internal static class Beta { public static int Use() => Alpha.Use(); }
'@ | Set-Content src/Fleet/Features/Probe/BetaSlice/Beta.cs

dotnet test --filter SliceBoundaryTests
```

Expected: **3 failed, 3 passed** — `Features_never_reference_Platform` and
`Only_Program_references_Platform_implementations` naming `AlphaSlice/Alpha.cs`,
and `No_slice_references_another_slice` naming `BetaSlice/Beta.cs`.

The probe must actually compile, which is why `Platform/Probe/ProbeThing.cs`
exists — a `using` of a namespace that does not exist fails the build instead of
the test, and proves nothing.

Then remove them and confirm green:

```powershell
Remove-Item -Recurse -Force src/Fleet/Features, src/Fleet/Platform
dotnet test
```

- [ ] **Step 3: Commit**

```powershell
git add tests/Fleet.Tests/Architecture tests/Fleet.Tests/Fleet.Tests.csproj
git commit -m "test: enforce slice boundaries from source text"
```

---

### Task 4: Shared pure helpers

**Files:**
- Create: `src/Fleet/Shared/ProjectName.cs`, `src/Fleet/Shared/HomePath.cs`
- Test: `tests/Fleet.Tests/Shared/ProjectNameTests.cs`, `tests/Fleet.Tests/Shared/HomePathTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Fleet.Shared;

public class ProjectNameTests
{
    [Theory]
    [InlineData("backend", "backend")]
    [InlineData("My Project", "MyProject")]
    [InlineData("a/b\\c", "abc")]
    [InlineData("web-app_2", "web-app_2")]
    [InlineData("...", "")]
    public void Sanitize_keeps_only_filename_safe_characters(string input, string expected)
        => Assert.Equal(expected, ProjectName.Sanitize(input));
}
```

```csharp
using Fleet.Shared;

public class HomePathTests
{
    [Fact]
    public void Contract_then_expand_round_trips_a_path_under_home()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, "repos", "backend");

        var contracted = HomePath.Contract(path);

        Assert.StartsWith("~", contracted);
        Assert.Equal(path, HomePath.Expand(contracted));
    }

    [Fact]
    public void Contract_leaves_a_path_outside_home_alone()
    {
        // NOT Path.GetTempPath(): on Windows that is
        // C:\Users\<user>\AppData\Local\Temp, which IS under home. Contract would
        // correctly return a "~" path and the test would assert the wrong thing.
        var root = Path.GetPathRoot(Environment.CurrentDirectory)
                   ?? Path.DirectorySeparatorChar.ToString();
        var path = Path.Combine(root, "fleet-outside-home");

        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), HomePath.Contract(path));
    }
}
```

Worth adding alongside these, and done during implementation: `Sanitize` cannot
produce a path traversal (`..`, `../../etc`, `C:\Windows` all lose every
separator and dot), `Contract` reduces the home directory itself to `~`, and
`Expand` leaves both a tilde-free path and a directory genuinely named
`~backup` alone.

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test --filter "ProjectNameTests|HomePathTests"`
Expected: FAIL — types not found

- [ ] **Step 3: Implement**

```csharp
using System.Text;

namespace Fleet.Shared;

public static class ProjectName
{
    /// <summary>
    /// Strips anything with no business in a filename. Returns an empty string
    /// when nothing usable is left, which callers must treat as an error rather
    /// than writing a file called ".json".
    /// </summary>
    public static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            if (char.IsAsciiLetterOrDigit(c) || c is '_' or '-') sb.Append(c);
        return sb.ToString();
    }
}
```

```csharp
namespace Fleet.Shared;

/// <summary>Stores paths under the home directory as "~/..." so projects are portable between machines.</summary>
public static class HomePath
{
    public static string Contract(string path)
    {
        var home = Home();
        if (string.IsNullOrEmpty(home)) return path;

        var clean = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var cleanHome = Path.TrimEndingDirectorySeparator(Path.GetFullPath(home));

        if (string.Equals(clean, cleanHome, Comparison)) return "~";

        var prefix = cleanHome + Path.DirectorySeparatorChar;
        return clean.StartsWith(prefix, Comparison)
            ? "~" + Path.DirectorySeparatorChar + clean[prefix.Length..]
            : clean;
    }

    public static string Expand(string path)
    {
        if (path != "~" && !path.StartsWith("~/") && !path.StartsWith("~\\")) return path;

        var home = Home();
        if (string.IsNullOrEmpty(home)) return path;
        return path == "~" ? home : Path.Combine(home, path[2..]);
    }

    // Windows paths are case-insensitive; Linux paths are not.
    private static StringComparison Comparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
}
```

- [ ] **Step 4: Run to confirm they pass**

Run: `dotnet test --filter "ProjectNameTests|HomePathTests"`
Expected: PASS, 7 tests

- [ ] **Step 5: Commit**

```powershell
git add src/Fleet/Shared tests/Fleet.Tests/Shared
git commit -m "feat: shared pure helpers for names and home paths"
```

---

### Task 5: Ports

The four interfaces every slice may depend on, and the types that cross them. Nothing here does I/O.

**Files:**
- Create: `src/Fleet/Ports/IFleetLog.cs`, `Ports/Git/IGitRunner.cs`, `Ports/Projects/IProjectStore.cs`, `Ports/Mux/IMuxDriver.cs`

- [ ] **Step 1: Write the log and git ports**

```csharp
namespace Fleet.Ports;

/// <summary>
/// Silent must not mean invisible: everything the fail-silent layer swallows
/// lands here, and `doctor` reports it.
/// </summary>
public interface IFleetLog
{
    void Swallowed(Exception e);
    void Write(string line);
    IReadOnlyList<string> Tail(int lines);
}
```

```csharp
namespace Fleet.Ports.Git;

public sealed record GitResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Ok => ExitCode == 0;
    public string Out => StdOut.Trim();

    /// <summary>git's own message is what diagnoses a bad ref or a locked worktree.</summary>
    public string Message => StdErr.Trim().Length > 0 ? StdErr.Trim() : $"exit {ExitCode}";
}

public interface IGitRunner
{
    /// <summary>Runs git in <paramref name="workDir"/>. stdin is optional; plumbing needs it.</summary>
    Task<GitResult> RunAsync(string workDir, IReadOnlyList<string> args,
                             string? stdin = null, CancellationToken ct = default);
}
```

- [ ] **Step 2: Write the projects port**

```csharp
namespace Fleet.Ports.Projects;

/// <summary>A named root folder whose children are repositories.</summary>
public sealed record Project(string Name, string Root);

public interface IProjectStore
{
    Project? Load(string name);
    IReadOnlyList<Project> List();
    void Save(Project project);
    void Remove(string name);
}
```

- [ ] **Step 3: Write the mux port**

Phase 1 defines the six verbs it needs. The full surface is in `docs/DESIGN.md`; adding methods nothing calls would only produce `NotImplementedException` landmines.

```csharp
namespace Fleet.Ports.Mux;

/// <summary>
/// Opaque by design. tmux pane ids look like "%12", WezTerm's are integers, the
/// embedded driver will use its own. A numeric type would bake one in.
/// </summary>
public readonly record struct PaneId(string Value)
{
    public override string ToString() => Value;
    public static readonly PaneId None = new("");
    public bool IsNone => string.IsNullOrEmpty(Value);
}

public sealed record Pane(
    PaneId Id, string WindowId, string SessionName, string Title, string Cwd, bool IsActive);

public sealed record SpawnOptions
{
    public string? Cwd { get; init; }
    /// <summary>tmux session / WezTerm workspace. Honoured only with NewWindow.</summary>
    public string? SessionName { get; init; }
    public bool NewWindow { get; init; }
    /// <summary>Command line to run. Empty means the default shell.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];
}

public enum SplitDirection { Right, Left, Top, Bottom }

public sealed record SplitOptions(PaneId Source, SplitDirection Direction)
{
    /// <summary>Size of the NEW pane as a percentage. Zero leaves the mux default.</summary>
    public int Percent { get; init; }
    public string? Cwd { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
}

[Flags]
public enum MuxCaps
{
    None = 0, Split = 1 << 0, Zoom = 1 << 1, Detach = 1 << 2, Persist = 1 << 3, Popup = 1 << 4,
}

/// <summary>The multiplexer could not be reached: not installed, or not running.</summary>
public sealed class MuxUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

public interface IMuxDriver
{
    string Name { get; }
    MuxCaps Caps { get; }

    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default);
    Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default);
    Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default);
    Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default);
    Task FocusPaneAsync(PaneId id, CancellationToken ct = default);

    /// <summary>The pane fleet is itself running in, or PaneId.None.</summary>
    PaneId CurrentPane { get; }
}
```

- [ ] **Step 4: Verify the boundary test still passes**

Run: `dotnet test --filter SliceBoundaryTests`
Expected: PASS — `Ports_depend_on_nothing_but_Shared` now has real files to check.

- [ ] **Step 5: Commit**

```powershell
git add src/Fleet/Ports
git commit -m "feat: four ports for all I/O"
```

---

## Chunk 2: Platform adapters

### Task 6: Storage — paths, JSON context, project store

**Files:**
- Create: `src/Fleet/Platform/Storage/FleetPaths.cs`, `FleetJsonContext.cs`, `JsonProjectStore.cs`
- Test: `tests/Fleet.Tests/Platform/Storage/JsonProjectStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Platform.Storage;
using Fleet.Ports.Projects;

public sealed class JsonProjectStoreTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public JsonProjectStoreTests()
        => Environment.SetEnvironmentVariable(FleetPaths.OverrideVariable, _tmp);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(FleetPaths.OverrideVariable, null);
        if (Directory.Exists(_tmp)) Directory.Delete(_tmp, recursive: true);
    }

    [Fact]
    public void Save_then_Load_round_trips()
    {
        var store = new JsonProjectStore();
        store.Save(new Project("backend", Path.GetTempPath()));

        var loaded = store.Load("backend");

        Assert.NotNull(loaded);
        Assert.Equal("backend", loaded!.Name);
        Assert.Equal(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())), loaded.Root);
    }

    [Fact]
    public void Load_returns_null_for_an_unknown_project()
        => Assert.Null(new JsonProjectStore().Load("nope"));

    [Fact]
    public void List_returns_projects_sorted_by_name()
    {
        var store = new JsonProjectStore();
        store.Save(new Project("zeta", Path.GetTempPath()));
        store.Save(new Project("alpha", Path.GetTempPath()));

        Assert.Equal(new[] { "alpha", "zeta" }, store.List().Select(p => p.Name));
    }

    [Fact]
    public void Save_rejects_a_name_that_sanitizes_to_nothing()
        => Assert.Throws<ArgumentException>(
            () => new JsonProjectStore().Save(new Project("...", Path.GetTempPath())));

    [Fact]
    public void List_skips_an_unreadable_file_rather_than_failing()
    {
        var store = new JsonProjectStore();
        store.Save(new Project("good", Path.GetTempPath()));
        File.WriteAllText(Path.Combine(FleetPaths.Projects, "broken.json"), "{ not json");

        Assert.Equal(new[] { "good" }, store.List().Select(p => p.Name));
    }
}
```

The last test is the fail-silent invariant at the storage layer: one corrupt file must not hide every other project.

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter JsonProjectStoreTests`
Expected: FAIL — types not found

- [ ] **Step 3: Implement paths**

```csharp
namespace Fleet.Platform.Storage;

public static class FleetPaths
{
    /// <summary>Set to relocate everything. Tests rely on this.</summary>
    public const string OverrideVariable = "FLEET_CONFIG_HOME";

    public static string Config
    {
        get
        {
            var over = Environment.GetEnvironmentVariable(OverrideVariable);
            if (!string.IsNullOrWhiteSpace(over)) return over;

            // ApplicationData is %APPDATA% on Windows and honours XDG_CONFIG_HOME
            // (defaulting to ~/.config) on Linux.
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fleet");
        }
    }

    public static string Projects => Path.Combine(Config, "projects");
    public static string Sessions => Path.Combine(Config, "sessions");
    public static string LogFile => Path.Combine(Config, "fleet.log");

    public static void EnsureDirs()
    {
        Directory.CreateDirectory(Projects);
        Directory.CreateDirectory(Sessions);
    }
}
```

- [ ] **Step 4: Implement the serializer context**

Reflection-based serialization is not AOT-safe, so every serialized type is declared here.

```csharp
using System.Text.Json.Serialization;

namespace Fleet.Platform.Storage;

/// <summary>On-disk shape. Versioned so a later format change is detectable.</summary>
public sealed class ProjectFile
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Root { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ProjectFile))]
public partial class FleetJsonContext : JsonSerializerContext;
```

- [ ] **Step 5: Implement the store**

```csharp
using System.Text.Json;
using Fleet.Ports.Projects;
using Fleet.Shared;

namespace Fleet.Platform.Storage;

public sealed class JsonProjectStore : IProjectStore
{
    private static string FileFor(string sanitized) => Path.Combine(FleetPaths.Projects, sanitized + ".json");

    public void Save(Project project)
    {
        var name = ProjectName.Sanitize(project.Name);
        if (name.Length == 0)
            throw new ArgumentException($"'{project.Name}' leaves no usable project name", nameof(project));

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(project.Root));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"{root} is not a directory");

        FleetPaths.EnsureDirs();

        var file = new ProjectFile { Name = name, Root = HomePath.Contract(root) };
        File.WriteAllText(FileFor(name), JsonSerializer.Serialize(file, FleetJsonContext.Default.ProjectFile));
    }

    public Project? Load(string name)
    {
        var sanitized = ProjectName.Sanitize(name);
        if (sanitized.Length == 0) return null;

        try
        {
            var file = JsonSerializer.Deserialize(
                File.ReadAllText(FileFor(sanitized)), FleetJsonContext.Default.ProjectFile);

            if (file is null || string.IsNullOrWhiteSpace(file.Root)) return null;

            return new Project(
                string.IsNullOrWhiteSpace(file.Name) ? sanitized : file.Name,
                HomePath.Expand(file.Root));
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<Project> List()
    {
        if (!Directory.Exists(FleetPaths.Projects)) return [];

        return Directory.EnumerateFiles(FleetPaths.Projects, "*.json")
            .Select(f => Load(Path.GetFileNameWithoutExtension(f)))
            .OfType<Project>()
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void Remove(string name)
    {
        var sanitized = ProjectName.Sanitize(name);
        if (sanitized.Length == 0) return;
        try { File.Delete(FileFor(sanitized)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
```

- [ ] **Step 6: Run to confirm it passes**

Run: `dotnet test --filter JsonProjectStoreTests`
Expected: PASS, 5 tests

- [ ] **Step 7: Commit**

```powershell
git add src/Fleet/Platform/Storage tests/Fleet.Tests/Platform/Storage
git commit -m "feat: AOT-safe JSON project store"
```

---

### Task 7: File log and git runner

**Files:**
- Create: `src/Fleet/Platform/Logging/FileLog.cs`, `src/Fleet/Platform/Git/GitRunner.cs`

- [ ] **Step 1: Implement the log**

```csharp
using Fleet.Platform.Storage;
using Fleet.Ports;

namespace Fleet.Platform.Logging;

public sealed class FileLog : IFleetLog
{
    public void Swallowed(Exception e) => Write($"swallowed: {e.GetType().Name}: {e.Message}");

    public void Write(string line)
    {
        try
        {
            FleetPaths.EnsureDirs();
            File.AppendAllText(FleetPaths.LogFile, $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Logging must never be the thing that breaks a command.
        }
    }

    public IReadOnlyList<string> Tail(int lines)
    {
        try { return File.ReadLines(FleetPaths.LogFile).TakeLast(lines).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }
}
```

- [ ] **Step 2: Implement the git runner**

```csharp
using System.Diagnostics;
using Fleet.Ports.Git;

namespace Fleet.Platform.Git;

public sealed class GitRunner(string executable = "git") : IGitRunner
{
    public async Task<GitResult> RunAsync(string workDir, IReadOnlyList<string> args,
                                          string? stdin = null, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin).ConfigureAwait(false);
            process.StandardInput.Close();
        }

        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct).ConfigureAwait(false);

        return new GitResult(process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s)`

- [ ] **Step 4: Commit**

```powershell
git add src/Fleet/Platform/Logging src/Fleet/Platform/Git
git commit -m "feat: file log and git runner adapters"
```

---

### Task 8: Fake mux driver

Built before the real driver, because it is what makes every slice testable with no terminal on either OS.

**Files:**
- Create: `src/Fleet/Platform/Mux/Fake/FakeMuxDriver.cs`
- Test: `tests/Fleet.Tests/Platform/Mux/Fake/FakeMuxDriverTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux;

public class FakeMuxDriverTests
{
    [Fact]
    public async Task Spawn_creates_a_pane_in_a_new_window()
    {
        var mux = new FakeMuxDriver();

        var id = await mux.SpawnAsync(new SpawnOptions
        {
            NewWindow = true, SessionName = "backend", Cwd = "/repos/backend", Args = ["claude"],
        });

        var pane = Assert.Single(await mux.ListPanesAsync());
        Assert.Equal(id, pane.Id);
        Assert.Equal("backend", pane.SessionName);
        Assert.Equal("/repos/backend", pane.Cwd);
        Assert.Equal(["claude"], mux.ArgsFor(id));
    }

    [Fact]
    public async Task Split_puts_the_new_pane_in_the_same_window()
    {
        var mux = new FakeMuxDriver();
        var left = await mux.SpawnAsync(new SpawnOptions { NewWindow = true, Args = ["claude"] });

        var right = await mux.SplitAsync(new SplitOptions(left, SplitDirection.Right)
        {
            Percent = 50, Args = ["fleet", "dash"],
        });

        var panes = await mux.ListPanesAsync();
        Assert.Equal(2, panes.Count);
        Assert.Equal(panes.Single(p => p.Id == left).WindowId,
                     panes.Single(p => p.Id == right).WindowId);
    }

    [Fact]
    public async Task Split_of_an_unknown_pane_throws()
    {
        var mux = new FakeMuxDriver();
        await Assert.ThrowsAsync<MuxUnavailableException>(
            () => mux.SplitAsync(new SplitOptions(new PaneId("nope"), SplitDirection.Right)));
    }

    [Fact]
    public async Task Focus_marks_exactly_one_pane_active()
    {
        var mux = new FakeMuxDriver();
        var a = await mux.SpawnAsync(new SpawnOptions { NewWindow = true });
        var b = await mux.SplitAsync(new SplitOptions(a, SplitDirection.Right));

        await mux.FocusPaneAsync(a);

        var panes = await mux.ListPanesAsync();
        Assert.True(panes.Single(p => p.Id == a).IsActive);
        Assert.False(panes.Single(p => p.Id == b).IsActive);
    }

    [Fact]
    public async Task IsAvailable_can_be_turned_off_to_simulate_a_missing_mux()
        => Assert.False(await new FakeMuxDriver { Available = false }.IsAvailableAsync());
}
```

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter FakeMuxDriverTests`
Expected: FAIL — `FakeMuxDriver` not found

- [ ] **Step 3: Implement**

```csharp
using System.Collections.Concurrent;
using Fleet.Ports.Mux;

namespace Fleet.Platform.Mux.Fake;

/// <summary>
/// Deterministic in-memory multiplexer. Ids are sequential ("p1", "p2", ...) so
/// assertions can be written against them.
/// </summary>
public sealed class FakeMuxDriver : IMuxDriver
{
    private readonly ConcurrentDictionary<string, Entry> _panes = new();
    private int _nextPane;
    private int _nextWindow;

    private sealed record Entry(Pane Pane, IReadOnlyList<string> Args);

    public string Name => "fake";
    public MuxCaps Caps => MuxCaps.Split | MuxCaps.Zoom | MuxCaps.Persist;

    /// <summary>Flip to false to simulate a multiplexer that is not installed.</summary>
    public bool Available { get; set; } = true;

    public PaneId CurrentPane { get; set; } = PaneId.None;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(Available);

    public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default)
    {
        RequireAvailable();
        IReadOnlyList<Pane> panes = _panes.Values
            .Select(e => e.Pane)
            .OrderBy(p => p.Id.Value, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult(panes);
    }

    public Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
    {
        RequireAvailable();

        var id = new PaneId($"p{Interlocked.Increment(ref _nextPane)}");
        var window = options.NewWindow
            ? $"w{Interlocked.Increment(ref _nextWindow)}"
            : _panes.Values.FirstOrDefault()?.Pane.WindowId ?? $"w{Interlocked.Increment(ref _nextWindow)}";

        Add(id, window, options.SessionName ?? "default", options.Cwd ?? "", options.Args);
        return Task.FromResult(id);
    }

    public Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default)
    {
        RequireAvailable();

        if (!_panes.TryGetValue(options.Source.Value, out var source))
            throw new MuxUnavailableException($"no pane {options.Source}");

        var id = new PaneId($"p{Interlocked.Increment(ref _nextPane)}");
        Add(id, source.Pane.WindowId, source.Pane.SessionName, options.Cwd ?? source.Pane.Cwd, options.Args);
        return Task.FromResult(id);
    }

    public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
    {
        RequireAvailable();
        Mutate(id, p => p with { Title = title });
        return Task.CompletedTask;
    }

    public Task FocusPaneAsync(PaneId id, CancellationToken ct = default)
    {
        RequireAvailable();
        if (!_panes.ContainsKey(id.Value)) throw new MuxUnavailableException($"no pane {id}");

        foreach (var key in _panes.Keys)
            Mutate(new PaneId(key), p => p with { IsActive = key == id.Value });

        return Task.CompletedTask;
    }

    /// <summary>Test helper: the command line a pane was created with.</summary>
    public IReadOnlyList<string> ArgsFor(PaneId id) => _panes.TryGetValue(id.Value, out var e) ? e.Args : [];

    private void Add(PaneId id, string window, string session, string cwd, IReadOnlyList<string> args)
        => _panes[id.Value] = new Entry(new Pane(id, window, session, "", cwd, true), args);

    private void Mutate(PaneId id, Func<Pane, Pane> change)
    {
        if (_panes.TryGetValue(id.Value, out var e)) _panes[id.Value] = e with { Pane = change(e.Pane) };
    }

    private void RequireAvailable()
    {
        if (!Available) throw new MuxUnavailableException("fake mux is marked unavailable");
    }
}
```

- [ ] **Step 4: Run to confirm it passes**

Run: `dotnet test --filter FakeMuxDriverTests`
Expected: PASS, 5 tests

- [ ] **Step 5: Commit**

```powershell
git add src/Fleet/Platform/Mux/Fake tests/Fleet.Tests/Platform/Mux/Fake
git commit -m "feat: in-memory fake mux driver"
```

---

### Task 9: `FailSilentDriver` and driver selection

**Files:**
- Create: `src/Fleet/Platform/Mux/FailSilentDriver.cs`, `src/Fleet/Platform/Mux/DriverSelector.cs`
- Test: `tests/Fleet.Tests/Platform/Mux/FailSilentDriverTests.cs`, `DriverSelectorTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux;

public class FailSilentDriverTests
{
    [Fact]
    public async Task Reads_degrade_to_empty_when_the_mux_is_unavailable()
    {
        var swallowed = new List<Exception>();
        var mux = new FailSilentDriver(new FakeMuxDriver { Available = false }, swallowed.Add);

        Assert.Empty(await mux.ListPanesAsync());
        Assert.Equal(PaneId.None, await mux.SpawnAsync(new SpawnOptions()));
        await mux.SetTitleAsync(new PaneId("p1"), "x");   // must not throw

        Assert.Equal(3, swallowed.Count);
    }

    [Fact]
    public async Task Successful_calls_pass_straight_through()
    {
        var mux = new FailSilentDriver(new FakeMuxDriver(), _ => { });
        Assert.False((await mux.SpawnAsync(new SpawnOptions { NewWindow = true })).IsNone);
        Assert.Single(await mux.ListPanesAsync());
    }

    [Fact]
    public async Task Programmer_errors_are_not_swallowed()
    {
        var mux = new FailSilentDriver(new ThrowingDriver(new ArgumentNullException("x")), _ => { });
        await Assert.ThrowsAsync<ArgumentNullException>(() => mux.ListPanesAsync());
    }

    private sealed class ThrowingDriver(Exception toThrow) : IMuxDriver
    {
        public string Name => "throwing";
        public MuxCaps Caps => MuxCaps.None;
        public PaneId CurrentPane => PaneId.None;
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => throw toThrow;
        public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default) => throw toThrow;
        public Task<PaneId> SpawnAsync(SpawnOptions o, CancellationToken ct = default) => throw toThrow;
        public Task<PaneId> SplitAsync(SplitOptions o, CancellationToken ct = default) => throw toThrow;
        public Task SetTitleAsync(PaneId id, string t, CancellationToken ct = default) => throw toThrow;
        public Task FocusPaneAsync(PaneId id, CancellationToken ct = default) => throw toThrow;
    }
}
```

The third test is the important one. "Swallow everything" is its own bug: a null argument is a defect, not a closed terminal.

```csharp
using Fleet.Platform.Mux;

public class DriverSelectorTests
{
    private static readonly HashSet<string> BothInstalled = ["wezterm", "tmux"];

    [Fact]
    public void Explicit_override_wins_over_everything()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            Override = "tmux", InsideTmux = true, InsideWezTerm = true,
            GuiReachable = true, Installed = BothInstalled,
        }));

    [Fact]
    public void Inside_tmux_adopts_tmux_even_though_wezterm_is_the_base()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            InsideTmux = true, GuiReachable = true, Installed = BothInstalled,
        }));

    [Fact]
    public void Inside_wezterm_adopts_wezterm()
        => Assert.Equal("wezterm", DriverSelector.Choose(new MuxEnvironment
        {
            InsideWezTerm = true, Installed = BothInstalled,
        }));

    [Fact]
    public void Launching_with_a_gui_prefers_wezterm()
        => Assert.Equal("wezterm", DriverSelector.Choose(new MuxEnvironment
        {
            GuiReachable = true, Installed = BothInstalled,
        }));

    [Fact]
    public void Launching_without_a_gui_falls_back_to_tmux()
        => Assert.Equal("tmux", DriverSelector.Choose(new MuxEnvironment
        {
            GuiReachable = false, Installed = BothInstalled,
        }));

    [Fact]
    public void Launching_with_nothing_installed_falls_through_to_embedded()
        => Assert.Equal("embedded", DriverSelector.Choose(new MuxEnvironment
        {
            GuiReachable = true, Installed = [],
        }));
}
```

The fifth is the one that matters: SSH into a Linux box loses `DISPLAY`, so it lands on tmux with no configuration and no OS sniffing.

- [ ] **Step 2: Run to confirm they fail**

Run: `dotnet test --filter "FailSilentDriverTests|DriverSelectorTests"`
Expected: FAIL — types not found

- [ ] **Step 3: Implement the decorator**

```csharp
using Fleet.Ports.Mux;

namespace Fleet.Platform.Mux;

/// <summary>
/// Enforces fleet's fail-silent invariant once, instead of at every call site.
///
/// Reads degrade to empty results and writes become no-ops when the multiplexer
/// is unreachable. Only expected failure types are caught: a programmer error
/// still propagates, because a bad argument is a defect rather than a closed
/// terminal. Everything swallowed is reported so silent never means invisible.
///
/// Destructive operations must NOT be routed through this. A teardown that
/// silently "succeeds" while the worktree is still on disk is how state diverges
/// from reality.
/// </summary>
public sealed class FailSilentDriver(IMuxDriver inner, Action<Exception> onSwallowed) : IMuxDriver
{
    public string Name => inner.Name;
    public MuxCaps Caps => inner.Caps;
    public PaneId CurrentPane => inner.CurrentPane;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
        => Guard(() => inner.IsAvailableAsync(ct), false);

    public Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default)
        => Guard(() => inner.ListPanesAsync(ct), Array.Empty<Pane>());

    public Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
        => Guard(() => inner.SpawnAsync(options, ct), PaneId.None);

    public Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default)
        => Guard(() => inner.SplitAsync(options, ct), PaneId.None);

    public Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
        => Guard(() => inner.SetTitleAsync(id, title, ct));

    public Task FocusPaneAsync(PaneId id, CancellationToken ct = default)
        => Guard(() => inner.FocusPaneAsync(id, ct));

    private static bool IsExpected(Exception e) =>
        e is MuxUnavailableException or IOException or TimeoutException
          or OperationCanceledException or System.ComponentModel.Win32Exception;

    private async Task<T> Guard<T>(Func<Task<T>> call, T fallback)
    {
        try { return await call().ConfigureAwait(false); }
        catch (Exception e) when (IsExpected(e)) { onSwallowed(e); return fallback; }
    }

    private async Task Guard(Func<Task> call)
    {
        try { await call().ConfigureAwait(false); }
        catch (Exception e) when (IsExpected(e)) { onSwallowed(e); }
    }
}
```

- [ ] **Step 4: Implement selection**

```csharp
namespace Fleet.Platform.Mux;

/// <summary>Everything selection depends on, gathered so the decision itself stays pure.</summary>
public sealed record MuxEnvironment
{
    public string? Override { get; init; }
    public bool InsideTmux { get; init; }
    public bool InsideWezTerm { get; init; }
    public bool GuiReachable { get; init; }
    public IReadOnlySet<string> Installed { get; init; } = new HashSet<string>();

    /// <summary>
    /// Reads the real environment. GUI reachability is probed rather than inferred
    /// from the OS: Linux needs a display server, and a Windows SSH session has no
    /// visible desktop.
    /// </summary>
    public static MuxEnvironment Current(Func<string, bool> isInstalled) => new()
    {
        Override      = Environment.GetEnvironmentVariable("FLEET_MUX"),
        InsideTmux    = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TMUX")),
        InsideWezTerm = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEZTERM_PANE")),
        GuiReachable  = OperatingSystem.IsWindows()
            ? string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SSH_CONNECTION"))
            : !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
              || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")),
        Installed = new HashSet<string>(
            new[] { "wezterm", "tmux" }.Where(isInstalled), StringComparer.OrdinalIgnoreCase),
    };

    /// <summary>Is an executable on PATH? Tolerates malformed PATH entries.</summary>
    public static bool OnPath(string exe)
    {
        var names = OperatingSystem.IsWindows() ? new[] { exe + ".exe", exe } : [exe];

        return (Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [])
            .Any(dir =>
            {
                if (string.IsNullOrWhiteSpace(dir)) return false;
                try { return names.Any(n => File.Exists(Path.Combine(dir, n))); }
                catch (ArgumentException) { return false; }
            });
    }
}

public static class DriverSelector
{
    public const string WezTerm = "wezterm";
    public const string Tmux = "tmux";
    public const string Embedded = "embedded";

    public static string Choose(MuxEnvironment env)
    {
        if (!string.IsNullOrWhiteSpace(env.Override)) return env.Override.Trim().ToLowerInvariant();

        // Adopt: we are already inside a multiplexer, so preference does not apply.
        if (env.InsideTmux) return Tmux;
        if (env.InsideWezTerm) return WezTerm;

        // Launch: nothing around us, so pick. WezTerm base, tmux fallback.
        if (env.GuiReachable && env.Installed.Contains(WezTerm)) return WezTerm;
        if (env.Installed.Contains(Tmux)) return Tmux;
        return Embedded;
    }
}
```

- [ ] **Step 5: Run to confirm they pass**

Run: `dotnet test --filter "FailSilentDriverTests|DriverSelectorTests"`
Expected: PASS, 9 tests

- [ ] **Step 6: Commit**

```powershell
git add src/Fleet/Platform/Mux tests/Fleet.Tests/Platform/Mux
git commit -m "feat: fail-silent decorator and adopt-vs-launch selection"
```

---

### Task 10: WezTerm driver

**Files:**
- Create: `src/Fleet/Platform/Mux/WezTerm/CwdUrl.cs`, `WezTermCli.cs`, `WezTermPaneJson.cs`, `WezTermDriver.cs`
- Test: `tests/Fleet.Tests/Platform/Mux/WezTerm/CwdUrlTests.cs`

- [ ] **Step 1: Write the failing test**

WezTerm reports cwd as a URL. On Windows that is `file:///C:/repos/x` — three slashes — so trimming the scheme naively leaves `/C:/repos/x`, which git rejects. On Unix the same URL is `file:///home/red/x`, where the leading slash **is** the path and must stay. Already paid for in `fleet-win`; the test exists so it is not paid for twice.

```csharp
using Fleet.Platform.Mux.WezTerm;

public class CwdUrlTests
{
    [Theory]
    [InlineData("file:///C:/repos/fleet", "C:/repos/fleet")]
    [InlineData("file:///home/red/repos", "/home/red/repos")]
    [InlineData("file://hostname/C:/repos", "hostname/C:/repos")]
    [InlineData(@"C:\repos\fleet", "C:/repos/fleet")]
    [InlineData("", "")]
    public void Normalize_handles_both_platforms(string input, string expected)
        => Assert.Equal(expected, CwdUrl.Normalize(input));
}
```

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter CwdUrlTests`
Expected: FAIL — `CwdUrl` not found

- [ ] **Step 3: Implement normalization**

```csharp
namespace Fleet.Platform.Mux.WezTerm;

public static class CwdUrl
{
    /// <summary>
    /// Turns WezTerm's file:// cwd into a plain path. Backslashes fold to forward
    /// slashes so callers split on one separator.
    /// </summary>
    public static string Normalize(string cwd)
    {
        var s = cwd.Replace('\\', '/');
        if (s.StartsWith("file://", StringComparison.Ordinal)) s = s["file://".Length..];

        // A Windows drive letter arrives as "/C:/..."; drop the slash the URL adds.
        // Anything else keeps its leading slash, because on Unix it is the root.
        if (s.Length >= 3 && s[0] == '/' && s[2] == ':' && char.IsAsciiLetter(s[1])) s = s[1..];

        return s;
    }
}
```

- [ ] **Step 4: Implement CLI invocation**

No unit test: this is the process boundary, and faking `Process` to assert it was called proves nothing. Covered by `doctor` and by the manual walkthrough in Task 16.

```csharp
using System.Diagnostics;
using Fleet.Ports.Mux;

namespace Fleet.Platform.Mux.WezTerm;

/// <summary>Runs `wezterm cli ...` with a bounded timeout.</summary>
public sealed class WezTermCli(string executable = "wezterm")
{
    /// <summary>
    /// WezTerm answers in single-digit milliseconds. Anything slower means the mux
    /// is wedged, and degrading beats hanging a dashboard refresh.
    /// </summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);

    public async Task<string> RunAsync(IReadOnlyList<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("cli");
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };

        try
        {
            if (!process.Start()) throw new MuxUnavailableException("could not start wezterm");
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            throw new MuxUnavailableException("wezterm is not on PATH", e);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Timeout);

        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException($"wezterm cli {string.Join(' ', args)} timed out");
        }

        var output = await stdout.ConfigureAwait(false);
        var error = await stderr.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var message = string.IsNullOrWhiteSpace(error) ? $"exit {process.ExitCode}" : error.Trim();
            throw new MuxUnavailableException($"wezterm cli {args[0]}: {message}");
        }

        return output;
    }

    private static void TryKill(Process p)
    {
        try { p.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }
}
```

- [ ] **Step 5: Implement the JSON shape**

```csharp
using System.Text.Json.Serialization;

namespace Fleet.Platform.Mux.WezTerm;

/// <summary>One row of `wezterm cli list --format json`.</summary>
public sealed class WezTermPaneJson
{
    [JsonPropertyName("window_id")] public int WindowId { get; set; }
    [JsonPropertyName("tab_id")]    public int TabId { get; set; }
    [JsonPropertyName("pane_id")]   public int PaneId { get; set; }
    [JsonPropertyName("workspace")] public string Workspace { get; set; } = "";
    [JsonPropertyName("title")]     public string Title { get; set; } = "";
    [JsonPropertyName("tab_title")] public string TabTitle { get; set; } = "";
    [JsonPropertyName("cwd")]       public string Cwd { get; set; } = "";
    [JsonPropertyName("is_active")] public bool IsActive { get; set; }
}

[JsonSerializable(typeof(WezTermPaneJson[]))]
public partial class WezTermJsonContext : JsonSerializerContext;
```

- [ ] **Step 6: Implement the driver**

```csharp
using System.Text.Json;
using Fleet.Ports.Mux;

namespace Fleet.Platform.Mux.WezTerm;

public sealed class WezTermDriver(WezTermCli? cli = null) : IMuxDriver
{
    private readonly WezTermCli _cli = cli ?? new WezTermCli();

    public string Name => "wezterm";
    public MuxCaps Caps => MuxCaps.Split | MuxCaps.Zoom | MuxCaps.Persist;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            await _cli.RunAsync(["list", "--format", "json"], ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception e) when (e is MuxUnavailableException or TimeoutException)
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<Pane>> ListPanesAsync(CancellationToken ct = default)
    {
        var json = await _cli.RunAsync(["list", "--format", "json"], ct).ConfigureAwait(false);
        var rows = JsonSerializer.Deserialize(json, WezTermJsonContext.Default.WezTermPaneJsonArray) ?? [];

        return rows.Select(r => new Pane(
            Id: new PaneId(r.PaneId.ToString()),
            // WezTerm's "tab" is the unit fleet treats as a window: one project per tab.
            WindowId: r.TabId.ToString(),
            SessionName: r.Workspace,
            Title: string.IsNullOrEmpty(r.TabTitle) ? r.Title : r.TabTitle,
            Cwd: CwdUrl.Normalize(r.Cwd),
            IsActive: r.IsActive)).ToList();
    }

    public async Task<PaneId> SpawnAsync(SpawnOptions options, CancellationToken ct = default)
    {
        var args = new List<string> { "spawn" };
        if (!string.IsNullOrEmpty(options.Cwd)) { args.Add("--cwd"); args.Add(options.Cwd); }

        if (options.NewWindow)
        {
            args.Add("--new-window");
            // wezterm rejects --workspace combined with a tab spawn.
            if (!string.IsNullOrEmpty(options.SessionName))
            {
                args.Add("--workspace");
                args.Add(options.SessionName);
            }
        }

        if (options.Args.Count > 0) { args.Add("--"); args.AddRange(options.Args); }
        return ParsePaneId(await _cli.RunAsync(args, ct).ConfigureAwait(false));
    }

    public async Task<PaneId> SplitAsync(SplitOptions options, CancellationToken ct = default)
    {
        var args = new List<string>
        {
            "split-pane", "--pane-id", options.Source.Value, DirectionFlag(options.Direction),
        };
        if (options.Percent > 0) { args.Add("--percent"); args.Add(options.Percent.ToString()); }
        if (!string.IsNullOrEmpty(options.Cwd)) { args.Add("--cwd"); args.Add(options.Cwd); }
        if (options.Args.Count > 0) { args.Add("--"); args.AddRange(options.Args); }

        return ParsePaneId(await _cli.RunAsync(args, ct).ConfigureAwait(false));
    }

    public async Task SetTitleAsync(PaneId id, string title, CancellationToken ct = default)
        => await _cli.RunAsync(["set-tab-title", "--pane-id", id.Value, title], ct).ConfigureAwait(false);

    public async Task FocusPaneAsync(PaneId id, CancellationToken ct = default)
        => await _cli.RunAsync(["activate-pane", "--pane-id", id.Value], ct).ConfigureAwait(false);

    /// <summary>
    /// NOT zero when absent: WezTerm numbers panes from 0, so the first pane of a
    /// fresh window is pane 0. Treating 0 as "no pane" silently breaks hooks and
    /// doctor, but only on a freshly started terminal — which is when it is least
    /// expected.
    /// </summary>
    public PaneId CurrentPane
    {
        get
        {
            var raw = Environment.GetEnvironmentVariable("WEZTERM_PANE");
            return string.IsNullOrWhiteSpace(raw) ? PaneId.None : new PaneId(raw.Trim());
        }
    }

    private static string DirectionFlag(SplitDirection d) => d switch
    {
        SplitDirection.Right => "--right",
        SplitDirection.Left => "--left",
        SplitDirection.Top => "--top",
        SplitDirection.Bottom => "--bottom",
        _ => throw new ArgumentOutOfRangeException(nameof(d)),
    };

    private static PaneId ParsePaneId(string output)
    {
        var s = output.Trim();
        if (!int.TryParse(s, out _)) throw new MuxUnavailableException($"expected a pane id, got \"{s}\"");
        return new PaneId(s);
    }
}
```

- [ ] **Step 7: Verify no trim warnings**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s)`. An `IL2026`/`IL3050` here means a reflection-based serializer call slipped in.

- [ ] **Step 8: Run all tests**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 9: Commit**

```powershell
git add src/Fleet/Platform/Mux/WezTerm tests/Fleet.Tests/Platform/Mux/WezTerm
git commit -m "feat: wezterm mux driver"
```

---

## Chunk 3: Projects slices

### Task 11: `OpenProject` slice

Written first of the slices because it is the one with real logic and it is fully testable against the fake driver.

**Files:**
- Create: `src/Fleet/Features/Projects/OpenProject/OpenProject.cs`
- Test: `tests/Fleet.Tests/Features/Projects/OpenProject/OpenProjectTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Features.Projects.OpenProject;
using Fleet.Platform.Mux;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects;

public class OpenProjectTests
{
    private static readonly Project Backend = new("backend", "/repos/backend");

    private static OpenProjectHandler Handler(IMuxDriver mux) => new(mux);

    private static OpenProjectCommand Command => new(Backend, Harness: "claude", FleetExecutable: "fleet");

    [Fact]
    public async Task Opens_a_harness_pane_and_a_dashboard_pane_in_one_window()
    {
        var mux = new FakeMuxDriver();
        var result = await Handler(mux).HandleAsync(Command);

        Assert.True(result.Succeeded);
        var panes = await mux.ListPanesAsync();
        Assert.Equal(2, panes.Count);
        Assert.Single(panes.Select(p => p.WindowId).Distinct());
    }

    [Fact]
    public async Task The_left_pane_runs_the_harness()
    {
        var mux = new FakeMuxDriver();
        var result = await Handler(mux).HandleAsync(Command);

        Assert.Equal(["claude"], mux.ArgsFor(result.Value.HarnessPane));
    }

    [Fact]
    public async Task The_right_pane_runs_the_dashboard_for_this_project()
    {
        var mux = new FakeMuxDriver();
        var result = await Handler(mux).HandleAsync(Command);

        Assert.Equal(["fleet", "dash", "--project", "backend"], mux.ArgsFor(result.Value.DashPane));
    }

    [Fact]
    public async Task Both_panes_open_in_the_project_root()
    {
        var mux = new FakeMuxDriver();
        await Handler(mux).HandleAsync(Command);

        Assert.All(await mux.ListPanesAsync(), p => Assert.Equal("/repos/backend", p.Cwd));
    }

    [Fact]
    public async Task The_dashboard_pane_ends_up_focused()
    {
        var mux = new FakeMuxDriver();
        var result = await Handler(mux).HandleAsync(Command);

        var panes = await mux.ListPanesAsync();
        Assert.True(panes.Single(p => p.Id == result.Value.DashPane).IsActive);
    }

    [Fact]
    public async Task Reports_a_failure_when_the_mux_is_unavailable()
    {
        var mux = new FailSilentDriver(new FakeMuxDriver { Available = false }, _ => { });
        var result = await Handler(mux).HandleAsync(Command);

        Assert.False(result.Succeeded);
        Assert.Contains("did not respond", result.Error);
    }
}
```

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter OpenProjectTests`
Expected: FAIL — types not found

- [ ] **Step 3: Implement the slice**

```csharp
using Fleet.Ports.Mux;
using Fleet.Ports.Projects;
using Fleet.Shared;

namespace Fleet.Features.Projects.OpenProject;

/// <param name="Harness">The AI CLI for the left pane. Hardcoded to claude by the caller in phase 1.</param>
/// <param name="FleetExecutable">
/// The binary the dashboard pane should invoke. Must be this process's own path,
/// not the string "fleet" — during development nothing on PATH resolves.
/// </param>
public sealed record OpenProjectCommand(Project Project, string Harness, string FleetExecutable);

public sealed record OpenProjectResult(PaneId HarnessPane, PaneId DashPane);

/// <summary>
/// Opens a project: one window at the project root, the AI harness on the left,
/// the fleet dashboard on the right, focus on the dashboard.
/// </summary>
public sealed class OpenProjectHandler(IMuxDriver mux)
{
    public async Task<Result<OpenProjectResult>> HandleAsync(
        OpenProjectCommand command, CancellationToken ct = default)
    {
        var harnessPane = await mux.SpawnAsync(new SpawnOptions
        {
            NewWindow = true,
            SessionName = command.Project.Name,
            Cwd = command.Project.Root,
            Args = [command.Harness],
        }, ct).ConfigureAwait(false);

        // FailSilentDriver returns PaneId.None rather than throwing when the mux
        // is unreachable, so this is the failure check, not a null guard.
        if (harnessPane.IsNone) return Failure(command);

        await mux.SetTitleAsync(harnessPane, command.Project.Name, ct).ConfigureAwait(false);

        var dashPane = await mux.SplitAsync(new SplitOptions(harnessPane, SplitDirection.Right)
        {
            Percent = 50,
            Cwd = command.Project.Root,
            Args = [command.FleetExecutable, "dash", "--project", command.Project.Name],
        }, ct).ConfigureAwait(false);

        if (dashPane.IsNone) return Failure(command);

        await mux.FocusPaneAsync(dashPane, ct).ConfigureAwait(false);
        return Result<OpenProjectResult>.Ok(new OpenProjectResult(harnessPane, dashPane));
    }

    private Result<OpenProjectResult> Failure(OpenProjectCommand command) =>
        Result<OpenProjectResult>.Fail(
            $"could not open '{command.Project.Name}' — the {mux.Name} multiplexer did not respond. " +
            "Run 'fleet doctor'.");
}
```

- [ ] **Step 4: Run to confirm it passes**

Run: `dotnet test --filter OpenProjectTests`
Expected: PASS, 6 tests

- [ ] **Step 5: Confirm the boundary tests still pass**

Run: `dotnet test --filter SliceBoundaryTests`
Expected: PASS — this slice references only `Fleet.Ports` and `Fleet.Shared`.

- [ ] **Step 6: Commit**

```powershell
git add src/Fleet/Features/Projects/OpenProject tests/Fleet.Tests/Features/Projects/OpenProject
git commit -m "feat(OpenProject): harness pane plus dashboard pane in one window"
```

---

### Task 12: `CreateProject` slice

**Files:**
- Create: `src/Fleet/Features/Projects/CreateProject/CreateProject.cs`, `CreateProjectView.cs`
- Test: `tests/Fleet.Tests/Features/Projects/CreateProject/CreateProjectTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Features.Projects.CreateProject;
using Fleet.Platform.Storage;
using Fleet.Ports.Projects;

public sealed class CreateProjectTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public CreateProjectTests()
    {
        Environment.SetEnvironmentVariable(FleetPaths.OverrideVariable, _tmp);
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(FleetPaths.OverrideVariable, null);
        if (Directory.Exists(_tmp)) Directory.Delete(_tmp, recursive: true);
    }

    private CreateProjectHandler Handler() => new(new JsonProjectStore());

    [Fact]
    public void Creates_and_returns_the_saved_project()
    {
        var result = Handler().Handle(new CreateProjectCommand("My Backend", _tmp));

        Assert.True(result.Succeeded);
        Assert.Equal("MyBackend", result.Value.Name);
    }

    [Fact]
    public void Rejects_a_blank_name()
        => Assert.Contains("name", Handler().Handle(new CreateProjectCommand("  ", _tmp)).Error);

    [Fact]
    public void Rejects_a_name_with_no_usable_characters()
        => Assert.Contains("usable", Handler().Handle(new CreateProjectCommand("...", _tmp)).Error);

    [Fact]
    public void Rejects_a_root_that_does_not_exist()
    {
        var missing = Path.Combine(_tmp, "nope");
        Assert.Contains("does not exist", Handler().Handle(new CreateProjectCommand("x", missing)).Error);
    }
}
```

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter CreateProjectTests`
Expected: FAIL — types not found

- [ ] **Step 3: Implement the slice**

Validation lives in the handler, not the dialog, which is why it is testable without a terminal.

```csharp
using Fleet.Ports.Projects;
using Fleet.Shared;

namespace Fleet.Features.Projects.CreateProject;

public sealed record CreateProjectCommand(string Name, string Root);

public sealed class CreateProjectHandler(IProjectStore store)
{
    public Result<Project> Handle(CreateProjectCommand command)
    {
        var name = command.Name.Trim();
        var root = command.Root.Trim();

        if (name.Length == 0) return Result<Project>.Fail("A project name is required.");
        if (root.Length == 0) return Result<Project>.Fail("A root directory is required.");

        var sanitized = ProjectName.Sanitize(name);
        if (sanitized.Length == 0)
            return Result<Project>.Fail($"'{name}' contains no usable characters for a project name.");

        if (!Directory.Exists(root)) return Result<Project>.Fail($"{root} does not exist.");

        var project = new Project(sanitized, root);

        try
        {
            store.Save(project);
        }
        catch (Exception e) when (e is ArgumentException or DirectoryNotFoundException or IOException)
        {
            return Result<Project>.Fail(e.Message);
        }

        // Read back, so the caller gets exactly what was persisted.
        return store.Load(sanitized) is { } saved
            ? Result<Project>.Ok(saved)
            : Result<Project>.Fail($"'{sanitized}' was written but could not be read back.");
    }
}
```

- [ ] **Step 4: Implement the view**

```csharp
using Terminal.Gui;

namespace Fleet.Features.Projects.CreateProject;

/// <summary>Asks for a name and a root directory. Returns null on cancel.</summary>
public static class CreateProjectView
{
    public static CreateProjectCommand? Show(CreateProjectHandler handler, out Ports.Projects.Project? created)
    {
        var nameField = new TextField { X = 12, Y = 1, Width = Dim.Fill(2) };
        var rootField = new TextField { X = 12, Y = 3, Width = Dim.Fill(2) };
        var error = new Label
        {
            X = 1, Y = 5, Width = Dim.Fill(2), ColorScheme = Colors.ColorSchemes["Error"],
        };

        CreateProjectCommand? submitted = null;
        Ports.Projects.Project? result = null;

        var ok = new Button { Text = "_Create", IsDefault = true };
        var cancel = new Button { Text = "Cancel" };

        var dialog = new Dialog
        {
            Title = "New project", Width = 70, Height = 10, Buttons = [ok, cancel],
        };

        ok.Accepting += (_, e) =>
        {
            e.Cancel = true;   // stay open unless the handler accepts it

            var command = new CreateProjectCommand(nameField.Text, rootField.Text);
            var outcome = handler.Handle(command);

            if (!outcome.Succeeded) { error.Text = outcome.Error!; return; }

            submitted = command;
            result = outcome.Value;
            Application.RequestStop(dialog);
        };

        cancel.Accepting += (_, e) => { e.Cancel = true; Application.RequestStop(dialog); };

        dialog.Add(
            new Label { X = 1, Y = 1, Text = "Name:" }, nameField,
            new Label { X = 1, Y = 3, Text = "Root:" }, rootField,
            error);

        Application.Run(dialog);
        dialog.Dispose();

        created = result;
        return submitted;
    }
}
```

- [ ] **Step 5: Run to confirm it passes**

Run: `dotnet test --filter CreateProjectTests`
Expected: PASS, 4 tests

Note: `Terminal.Gui` is added to the project in Task 13 Step 1. If this task runs first, add it now:
`dotnet add src/Fleet package Terminal.Gui --version 2.4.17`

- [ ] **Step 6: Commit**

```powershell
git add src/Fleet/Features/Projects/CreateProject tests/Fleet.Tests/Features/Projects/CreateProject
git commit -m "feat(CreateProject): validated project creation with a dialog"
```

---

### Task 13: `PickProject` slice

**Files:**
- Modify: `src/Fleet/Fleet.csproj` — add Terminal.Gui
- Create: `src/Fleet/Features/Projects/PickProject/PickProject.cs`, `PickProjectView.cs`

- [ ] **Step 1: Add Terminal.Gui**

```powershell
dotnet add src/Fleet package Terminal.Gui --version 2.4.17
dotnet build
```

Expected: `Build succeeded. 0 Warning(s)`.

**This step answers an open question from `DESIGN.md`.** With `IsAotCompatible` on, any `IL2026`/`IL3050` warning here is Terminal.Gui's AOT compatibility failing. If that happens, stop and record it in the `DESIGN.md` verification log before continuing — it changes the stack, not just this task.

- [ ] **Step 2: Implement the slice**

The behaviour is small — list projects, offer a "new" entry — but keeping it out of the view means the entry list is testable and the view stays dumb.

```csharp
using Fleet.Ports.Projects;

namespace Fleet.Features.Projects.PickProject;

/// <summary>One row in the picker: either a saved project or the "new" entry.</summary>
public sealed record PickerEntry(string Label, Project? Project)
{
    public bool IsNew => Project is null;
}

public sealed class PickProjectHandler(IProjectStore store)
{
    public const string NewLabel = "＋  New project…";

    public IReadOnlyList<PickerEntry> Entries()
    {
        var entries = store.List()
            .Select(p => new PickerEntry($"{p.Name}   {p.Root}", p))
            .ToList();

        entries.Add(new PickerEntry(NewLabel, null));
        return entries;
    }
}
```

- [ ] **Step 3: Implement the view**

```csharp
using System.Collections.ObjectModel;
using Fleet.Features.Projects.CreateProject;
using Fleet.Ports.Projects;
using Terminal.Gui;

namespace Fleet.Features.Projects.PickProject;

public static class PickProjectView
{
    /// <summary>Runs the picker. Returns the chosen project, or null if the user quit.</summary>
    public static Project? Show(PickProjectHandler picker, CreateProjectHandler creator)
    {
        Project? chosen = null;
        var entries = picker.Entries();

        var list = new ListView
        {
            X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1),
            Source = new ListWrapper<string>(
                new ObservableCollection<string>(entries.Select(e => e.Label).ToList())),
        };

        var window = new Window
        {
            Title = "fleet — open a project",
            BorderStyle = LineStyle.Rounded,
        };

        var hint = new Label { X = 0, Y = Pos.AnchorEnd(1), Text = "⏎ open    n new    q quit" };

        void Accept()
        {
            var index = list.SelectedItem;
            if (index < 0 || index >= entries.Count) return;

            if (entries[index].IsNew)
            {
                CreateProjectView.Show(creator, out var created);
                if (created is null) return;   // cancelled

                chosen = created;
                Application.RequestStop(window);
                return;
            }

            chosen = entries[index].Project;
            Application.RequestStop(window);
        }

        list.OpenSelectedItem += (_, _) => Accept();

        window.KeyDown += (_, key) =>
        {
            if (key == Key.Q || key == Key.Esc)
            {
                Application.RequestStop(window);
                key.Handled = true;
            }
            else if (key == Key.N)
            {
                list.SelectedItem = entries.Count - 1;
                Accept();
                key.Handled = true;
            }
        };

        window.Add(list, hint);

        Application.Init();
        try { Application.Run(window); }
        finally { window.Dispose(); Application.Shutdown(); }

        return chosen;
    }
}
```

- [ ] **Step 4: Verify boundaries**

Run: `dotnet test --filter SliceBoundaryTests`
Expected: PASS.

Note: `PickProjectView` references `Fleet.Features.Projects.CreateProject`, which **is** a cross-slice reference and **will fail** `No_slice_references_another_slice`.

Resolve it by composition rather than by weakening the rule: change `PickProjectView.Show` to take a `Func<Project?>` for creating a project, and let `Program.cs` supply `() => { CreateProjectView.Show(creator, out var p); return p; }`. `Program.cs` is the composition root and is allowed to know both.

Apply that change now:

```csharp
    public static Project? Show(PickProjectHandler picker, Func<Project?> createProject)
```

and inside `Accept()`:

```csharp
            if (entries[index].IsNew)
            {
                var created = createProject();
                if (created is null) return;
                chosen = created;
                Application.RequestStop(window);
                return;
            }
```

Then remove the `using Fleet.Features.Projects.CreateProject;` line. Re-run the boundary test; it must pass.

This is the architecture test earning its place on the very first slice pair that could have leaked.

- [ ] **Step 5: Commit**

```powershell
git add src/Fleet/Features/Projects/PickProject src/Fleet/Fleet.csproj
git commit -m "feat(PickProject): picker slice, decoupled from CreateProject by a callback"
```

---

## Chunk 4: Repositories slices

### Task 14: `BranchSlug` — area-shared

Used by more than one slice in this area, so it lives in the area root rather than in `Shared/`.

**Files:**
- Create: `src/Fleet/Features/Repositories/BranchSlug.cs`
- Test: `tests/Fleet.Tests/Features/Repositories/BranchSlugTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Features.Repositories;

public class BranchSlugTests
{
    [Theory]
    [InlineData("main", "main")]
    [InlineData("feature/foo", "feature_foo")]
    [InlineData("a/b\\c", "a_b_c")]
    public void Slug_makes_a_branch_usable_as_a_directory_name(string branch, string expected)
        => Assert.Equal(expected, BranchSlug.Of(branch));
}
```

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter BranchSlugTests`
Expected: FAIL — `BranchSlug` not found

- [ ] **Step 3: Implement**

```csharp
namespace Fleet.Features.Repositories;

public static class BranchSlug
{
    /// <summary>Turns a branch name into a directory name: separators become underscores.</summary>
    public static string Of(string branch) => branch.Replace('/', '_').Replace('\\', '_');
}
```

- [ ] **Step 4: Run to confirm it passes**

Run: `dotnet test --filter BranchSlugTests`
Expected: PASS, 3 tests

- [ ] **Step 5: Commit**

```powershell
git add src/Fleet/Features/Repositories/BranchSlug.cs tests/Fleet.Tests/Features/Repositories/BranchSlugTests.cs
git commit -m "feat(Repositories): branch slug"
```

---

### Task 15: `AddRepository` slice

The trap: `git worktree add` fails on a freshly `init --bare` repository, because there is no HEAD commit. Plumbing solves it without depending on a git version that has `worktree add --orphan`:

```
git mktree             (empty stdin)  -> empty tree sha
git commit-tree <tree> -m "..."       -> commit sha
git update-ref refs/heads/<branch> <commit>
git symbolic-ref HEAD refs/heads/<branch>
git worktree add <dir> <branch>
```

**Files:**
- Create: `src/Fleet/Features/Repositories/AddRepository/AddRepository.cs`, `AddRepositoryView.cs`
- Test: `tests/Fleet.Tests/Features/Repositories/AddRepository/AddRepositoryTests.cs`

These tests run real git against temp directories. Mocks would prove nothing — the point is that the plumbing sequence actually works.

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Features.Repositories.AddRepository;
using Fleet.Platform.Git;

public sealed class AddRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public AddRepositoryTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static AddRepositoryHandler Handler() => new(new GitRunner());

    [Fact]
    public async Task Creating_a_new_repo_produces_a_bare_repo_with_a_default_branch_worktree()
    {
        var result = await Handler().HandleAsync(
            AddRepositoryCommand.CreateNew(_root, "widgets", "main"));

        Assert.True(result.Succeeded);
        Assert.Equal("widgets", result.Value.Name);

        var worktree = Path.Combine(_root, "widgets", "main");
        Assert.True(Directory.Exists(worktree));
        Assert.True(File.Exists(Path.Combine(worktree, ".git")));

        var head = await new GitRunner().RunAsync(worktree, ["rev-parse", "--abbrev-ref", "HEAD"]);
        Assert.Equal("main", head.Out);

        var bare = await new GitRunner().RunAsync(
            Path.Combine(_root, "widgets"), ["rev-parse", "--is-bare-repository"]);
        Assert.Equal("true", bare.Out);
    }

    [Fact]
    public async Task A_branch_with_a_slash_is_slugged_into_the_directory_name()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "widgets", "release/v1"));
        Assert.True(Directory.Exists(Path.Combine(_root, "widgets", "release_v1")));
    }

    [Fact]
    public async Task An_existing_name_is_refused()
    {
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "widgets", "main"));

        var again = await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "widgets", "main"));

        Assert.False(again.Succeeded);
        Assert.Contains("already exists", again.Error);
    }

    [Fact]
    public async Task Cloning_a_url_creates_the_default_branch_worktree()
    {
        // An origin to clone from: a bare repo with one commit.
        await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "origin.git", "main"));
        var origin = Path.Combine(_root, "origin.git");

        var dest = Path.Combine(_root, "dest");
        Directory.CreateDirectory(dest);

        var result = await Handler().HandleAsync(
            AddRepositoryCommand.Clone(dest, "widgets", origin, "main"));

        Assert.True(result.Succeeded);
        Assert.Equal("main", result.Value.DefaultBranch);
        Assert.True(Directory.Exists(Path.Combine(dest, "widgets", "main")));
    }

    [Fact]
    public async Task A_blank_name_is_refused_without_touching_the_disk()
    {
        var result = await Handler().HandleAsync(AddRepositoryCommand.CreateNew(_root, "  ", "main"));

        Assert.False(result.Succeeded);
        Assert.Empty(Directory.EnumerateDirectories(_root));
    }

    [Fact]
    public async Task A_clone_without_a_url_is_refused()
    {
        var result = await Handler().HandleAsync(AddRepositoryCommand.Clone(_root, "widgets", "", "main"));
        Assert.False(result.Succeeded);
        Assert.Contains("URL", result.Error);
    }
}
```

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter AddRepositoryTests`
Expected: FAIL — types not found

- [ ] **Step 3: Implement the slice**

```csharp
using Fleet.Ports.Git;
using Fleet.Shared;

namespace Fleet.Features.Repositories.AddRepository;

public enum AddRepositoryKind { CreateNew, CloneUrl }

/// <summary>A bare repository container: worktrees live as its children.</summary>
public sealed record Repository(string Name, string Path, string DefaultBranch);

public sealed record AddRepositoryCommand(
    AddRepositoryKind Kind, string ProjectRoot, string Name, string DefaultBranch, string? Url)
{
    public static AddRepositoryCommand CreateNew(string projectRoot, string name, string defaultBranch)
        => new(AddRepositoryKind.CreateNew, projectRoot, name, defaultBranch, null);

    public static AddRepositoryCommand Clone(string projectRoot, string name, string url, string defaultBranch)
        => new(AddRepositoryKind.CloneUrl, projectRoot, name, defaultBranch, url);
}

/// <summary>
/// Phase 1 supports one layout: a bare repository whose worktrees are its
/// children. The plain and worktree-container layouts in DESIGN.md are deferred.
/// </summary>
public sealed class AddRepositoryHandler(IGitRunner git)
{
    public async Task<Result<Repository>> HandleAsync(
        AddRepositoryCommand command, CancellationToken ct = default)
    {
        var name = command.Name.Trim();
        var branch = command.DefaultBranch.Trim();
        var url = command.Url?.Trim() ?? "";

        if (name.Length == 0) return Fail("A repository name is required.");
        if (branch.Length == 0) return Fail("A default branch is required.");
        if (command.Kind == AddRepositoryKind.CloneUrl && url.Length == 0)
            return Fail("A URL is required to clone a repository.");

        var bare = Path.Combine(command.ProjectRoot, name);
        if (Directory.Exists(bare)) return Fail($"{bare} already exists.");

        try
        {
            return command.Kind == AddRepositoryKind.CreateNew
                ? await CreateNewAsync(command.ProjectRoot, bare, name, branch, ct).ConfigureAwait(false)
                : await CloneAsync(command.ProjectRoot, bare, name, url, branch, ct).ConfigureAwait(false);
        }
        catch (GitFailedException e)
        {
            return Fail(e.Message);
        }
    }

    private async Task<Result<Repository>> CreateNewAsync(
        string projectRoot, string bare, string name, string branch, CancellationToken ct)
    {
        await Run(projectRoot, ["init", "--bare", "--initial-branch", branch, name], ct);
        await SeedInitialCommitAsync(bare, branch, ct);
        await AddWorktreeAsync(bare, branch, ct);

        return Result<Repository>.Ok(new Repository(name, bare, branch));
    }

    private async Task<Result<Repository>> CloneAsync(
        string projectRoot, string bare, string name, string url, string branch, CancellationToken ct)
    {
        await Run(projectRoot, ["clone", "--bare", url, name], ct);

        // `clone --bare` sets HEAD from the remote. Use the caller's branch when
        // given, otherwise whatever the remote considers its trunk.
        var effective = branch;
        if (effective.Length == 0)
        {
            var head = await git.RunAsync(bare, ["symbolic-ref", "--short", "HEAD"], null, ct)
                                .ConfigureAwait(false);
            effective = head.Ok && head.Out.Length > 0 ? head.Out : "main";
        }

        await AddWorktreeAsync(bare, effective, ct);
        return Result<Repository>.Ok(new Repository(name, bare, effective));
    }

    private async Task AddWorktreeAsync(string bare, string branch, CancellationToken ct)
    {
        var dir = Path.Combine(bare, BranchSlug.Of(branch));
        if (Directory.Exists(dir)) return;
        await Run(bare, ["worktree", "add", dir, branch], ct);
    }

    /// <summary>
    /// A freshly `init --bare` repository has no HEAD commit, and `git worktree add`
    /// refuses to branch from nothing. Plumbing creates an empty root commit without
    /// needing a working tree: an empty tree, a commit pointing at it, and the branch
    /// ref updated to that commit.
    /// </summary>
    private async Task SeedInitialCommitAsync(string bare, string branch, CancellationToken ct)
    {
        var tree = await git.RunAsync(bare, ["mktree"], stdin: "", ct).ConfigureAwait(false);
        if (!tree.Ok) throw new GitFailedException("mktree", tree);

        var commit = await git.RunAsync(bare, ["commit-tree", tree.Out, "-m", "Initial commit"], null, ct)
                              .ConfigureAwait(false);
        if (!commit.Ok) throw new GitFailedException("commit-tree", commit);

        await Run(bare, ["update-ref", $"refs/heads/{branch}", commit.Out], ct);
        await Run(bare, ["symbolic-ref", "HEAD", $"refs/heads/{branch}"], ct);
    }

    private async Task Run(string dir, string[] args, CancellationToken ct)
    {
        var r = await git.RunAsync(dir, args, null, ct).ConfigureAwait(false);
        if (!r.Ok) throw new GitFailedException(args[0], r);
    }

    private static Result<Repository> Fail(string reason) => Result<Repository>.Fail(reason);

    /// <summary>Internal control flow only; the handler converts it to a Result.</summary>
    private sealed class GitFailedException(string verb, GitResult result)
        : Exception($"git {verb}: {result.Message}");
}
```

Note on `init --bare --initial-branch`: `--initial-branch` needs git 2.28. `SeedInitialCommitAsync` sets `HEAD` explicitly afterwards regardless, so the flag is belt-and-braces rather than load-bearing.

- [ ] **Step 4: Implement the view**

```csharp
using Terminal.Gui;

namespace Fleet.Features.Repositories.AddRepository;

public static class AddRepositoryView
{
    /// <summary>Collects a request. Returns null on cancel. Validation is the handler's job.</summary>
    public static AddRepositoryCommand? Show(string projectRoot)
    {
        var kind = new RadioGroup
        {
            X = 12, Y = 1,
            RadioLabels = ["Create _new repository", "Clone from _URL"],
            SelectedItem = 0,
        };

        var nameField = new TextField { X = 12, Y = 4, Width = Dim.Fill(2) };
        var urlField = new TextField { X = 12, Y = 6, Width = Dim.Fill(2), Enabled = false };
        var branchField = new TextField { X = 12, Y = 8, Width = Dim.Fill(2), Text = "main" };

        kind.SelectedItemChanged += (_, _) => urlField.Enabled = kind.SelectedItem == 1;

        AddRepositoryCommand? result = null;

        var ok = new Button { Text = "_Add", IsDefault = true };
        var cancel = new Button { Text = "Cancel" };

        var dialog = new Dialog
        {
            Title = "Add repository", Width = 76, Height = 14, Buttons = [ok, cancel],
        };

        ok.Accepting += (_, e) =>
        {
            e.Cancel = true;

            result = kind.SelectedItem == 0
                ? AddRepositoryCommand.CreateNew(projectRoot, nameField.Text, branchField.Text)
                : AddRepositoryCommand.Clone(projectRoot, nameField.Text, urlField.Text, branchField.Text);

            Application.RequestStop(dialog);
        };

        cancel.Accepting += (_, e) => { e.Cancel = true; Application.RequestStop(dialog); };

        dialog.Add(
            new Label { X = 1, Y = 1, Text = "Kind:" }, kind,
            new Label { X = 1, Y = 4, Text = "Name:" }, nameField,
            new Label { X = 1, Y = 6, Text = "URL:" }, urlField,
            new Label { X = 1, Y = 8, Text = "Branch:" }, branchField);

        Application.Run(dialog);
        dialog.Dispose();
        return result;
    }
}
```

- [ ] **Step 5: Run to confirm it passes**

Run: `dotnet test --filter AddRepositoryTests`
Expected: PASS, 6 tests

- [ ] **Step 6: Commit**

```powershell
git add src/Fleet/Features/Repositories/AddRepository tests/Fleet.Tests/Features/Repositories/AddRepository
git commit -m "feat(AddRepository): create bare or clone, with a default-branch worktree"
```

---

### Task 16: `ListRepositories` slice

**Files:**
- Create: `src/Fleet/Features/Repositories/ListRepositories/ListRepositories.cs`
- Test: `tests/Fleet.Tests/Features/Repositories/ListRepositories/ListRepositoriesTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Platform.Git;

public sealed class ListRepositoriesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public ListRepositoriesTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public async Task Finds_bare_containers_and_ignores_plain_directories()
    {
        await new AddRepositoryHandler(new GitRunner())
            .HandleAsync(AddRepositoryCommand.CreateNew(_root, "widgets", "main"));
        Directory.CreateDirectory(Path.Combine(_root, "not-a-repo"));

        var repos = await new ListRepositoriesHandler(new GitRunner()).HandleAsync(_root);

        var repo = Assert.Single(repos);
        Assert.Equal("widgets", repo.Name);
        Assert.Equal("main", repo.DefaultBranch);
    }

    [Fact]
    public async Task Returns_empty_for_a_root_that_does_not_exist()
    {
        var repos = await new ListRepositoriesHandler(new GitRunner())
            .HandleAsync(Path.Combine(_root, "nope"));

        Assert.Empty(repos);
    }
}
```

Note: this test references `AddRepository` from `ListRepositories`' test file, which is fine — the architecture rule constrains `src/`, not tests. Tests are allowed to compose slices to build fixtures.

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter ListRepositoriesTests`
Expected: FAIL — `ListRepositoriesHandler` not found

- [ ] **Step 3: Implement**

`RepositorySummary` is deliberately its own type rather than reusing `AddRepository.Repository`: that would be a cross-slice reference, and the two will diverge anyway once agent counts appear in the list.

```csharp
using Fleet.Ports.Git;

namespace Fleet.Features.Repositories.ListRepositories;

public sealed record RepositorySummary(string Name, string Path, string DefaultBranch);

public sealed class ListRepositoriesHandler(IGitRunner git)
{
    /// <summary>
    /// Bare containers directly under the project root, sorted by name. Anything
    /// that is not a bare repository is skipped rather than reported: a stray
    /// directory in a project root is normal, not an error.
    /// </summary>
    public async Task<IReadOnlyList<RepositorySummary>> HandleAsync(
        string projectRoot, CancellationToken ct = default)
    {
        if (!Directory.Exists(projectRoot)) return [];

        var repos = new List<RepositorySummary>();

        foreach (var dir in Directory.EnumerateDirectories(projectRoot))
        {
            var isBare = await git.RunAsync(dir, ["rev-parse", "--is-bare-repository"], null, ct)
                                  .ConfigureAwait(false);
            if (!isBare.Ok || isBare.Out != "true") continue;

            var head = await git.RunAsync(dir, ["symbolic-ref", "--short", "HEAD"], null, ct)
                                .ConfigureAwait(false);

            repos.Add(new RepositorySummary(
                Path.GetFileName(dir), dir, head.Ok && head.Out.Length > 0 ? head.Out : "main"));
        }

        return repos.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
```

- [ ] **Step 4: Run to confirm it passes**

Run: `dotnet test --filter ListRepositoriesTests`
Expected: PASS, 2 tests

- [ ] **Step 5: Commit**

```powershell
git add src/Fleet/Features/Repositories/ListRepositories tests/Fleet.Tests/Features/Repositories/ListRepositories
git commit -m "feat(ListRepositories): enumerate bare containers in a project root"
```

---

## Chunk 5: Dashboard, diagnostics, composition

### Task 17: `RunDoctor` slice

The end-to-end smoke test, and the only thing the AOT CI job runs. It must never need a terminal, a GUI, or a project.

**Files:**
- Create: `src/Fleet/Features/Diagnostics/RunDoctor/RunDoctor.cs`
- Test: `tests/Fleet.Tests/Features/Diagnostics/RunDoctor/RunDoctorTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using Fleet.Features.Diagnostics.RunDoctor;
using Fleet.Platform.Mux.Fake;
using Fleet.Ports;
using Fleet.Ports.Projects;

public class RunDoctorTests
{
    private sealed class NullLog : IFleetLog
    {
        public void Swallowed(Exception e) { }
        public void Write(string line) { }
        public IReadOnlyList<string> Tail(int lines) => [];
    }

    private sealed class EmptyStore : IProjectStore
    {
        public Project? Load(string name) => null;
        public IReadOnlyList<Project> List() => [];
        public void Save(Project project) { }
        public void Remove(string name) { }
    }

    [Fact]
    public async Task Reports_healthy_when_the_mux_answers()
    {
        var report = await new RunDoctorHandler(
            new FakeMuxDriver(), new EmptyStore(), new NullLog(), gitVersion: () => Task.FromResult("git 2.49.0"))
            .HandleAsync(new RunDoctorCommand("fake", UnsupportedReason: null));

        Assert.True(report.Healthy);
        Assert.Empty(report.Problems);
    }

    [Fact]
    public async Task Reports_a_problem_when_the_mux_is_unreachable()
    {
        var report = await new RunDoctorHandler(
            new FakeMuxDriver { Available = false }, new EmptyStore(), new NullLog(),
            () => Task.FromResult("git 2.49.0"))
            .HandleAsync(new RunDoctorCommand("fake", null));

        Assert.False(report.Healthy);
        Assert.Contains(report.Problems, p => p.Contains("multiplexer"));
    }

    [Fact]
    public async Task Reports_a_problem_when_the_driver_is_not_implemented()
    {
        var report = await new RunDoctorHandler(
            new FakeMuxDriver(), new EmptyStore(), new NullLog(), () => Task.FromResult("git 2.49.0"))
            .HandleAsync(new RunDoctorCommand("tmux", "the 'tmux' driver is not implemented yet"));

        Assert.False(report.Healthy);
        Assert.Contains(report.Problems, p => p.Contains("tmux"));
    }

    [Fact]
    public async Task Reports_a_problem_when_git_is_missing()
    {
        var report = await new RunDoctorHandler(
            new FakeMuxDriver(), new EmptyStore(), new NullLog(), () => Task.FromResult<string?>(null))
            .HandleAsync(new RunDoctorCommand("fake", null));

        Assert.False(report.Healthy);
        Assert.Contains(report.Problems, p => p.Contains("git"));
    }
}
```

The handler returns a report rather than printing, which is what makes it testable. `Program.cs` does the printing.

- [ ] **Step 2: Run to confirm it fails**

Run: `dotnet test --filter RunDoctorTests`
Expected: FAIL — types not found

- [ ] **Step 3: Implement**

```csharp
using Fleet.Ports;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects;

namespace Fleet.Features.Diagnostics.RunDoctor;

public sealed record RunDoctorCommand(string ChosenDriver, string? UnsupportedReason);

public sealed record DoctorReport(
    string ChosenDriver,
    bool MuxReachable,
    string? GitVersion,
    IReadOnlyList<Project> Projects,
    IReadOnlyList<string> RecentSwallowed,
    IReadOnlyList<string> Problems)
{
    public bool Healthy => Problems.Count == 0;
}

/// <param name="gitVersion">Returns git's version string, or null when git is missing.</param>
public sealed class RunDoctorHandler(
    IMuxDriver mux, IProjectStore projects, IFleetLog log, Func<Task<string?>> gitVersion)
{
    public async Task<DoctorReport> HandleAsync(RunDoctorCommand command, CancellationToken ct = default)
    {
        var problems = new List<string>();

        if (command.UnsupportedReason is not null) problems.Add(command.UnsupportedReason);

        var reachable = await mux.IsAvailableAsync(ct).ConfigureAwait(false);
        if (!reachable)
            problems.Add($"the {command.ChosenDriver} multiplexer did not respond");

        var version = await gitVersion().ConfigureAwait(false);
        if (version is null) problems.Add("git was not found on PATH");

        return new DoctorReport(
            command.ChosenDriver, reachable, version,
            projects.List(), log.Tail(5), problems);
    }
}
```

- [ ] **Step 4: Run to confirm it passes**

Run: `dotnet test --filter RunDoctorTests`
Expected: PASS, 4 tests

- [ ] **Step 5: Commit**

```powershell
git add src/Fleet/Features/Diagnostics tests/Fleet.Tests/Features/Diagnostics
git commit -m "feat(RunDoctor): environment report as a value, not a print"
```

---

### Task 18: `ShowDashboard` slice

Two panes: repositories, populated; agents, a placeholder saying phase 2 brings them. The add-repository trigger is wired three ways as the spec requires — a **button**, a **keybind** (`a`), and the same code path both call.

**Files:**
- Create: `src/Fleet/Features/Dashboard/ShowDashboard/ShowDashboard.cs`, `ShowDashboardView.cs`

- [ ] **Step 1: Implement the slice's behaviour**

The dashboard's only real logic is turning a repository list into display rows, and that is worth having outside the view.

```csharp
namespace Fleet.Features.Dashboard.ShowDashboard;

public sealed record DashboardRow(string Text);

public static class DashboardRows
{
    public const string EmptyHint = "(no repositories — press 'a' to add one)";

    /// <summary>Name and default branch per row, or a single hint when there are none.</summary>
    public static IReadOnlyList<DashboardRow> ForRepositories(
        IReadOnlyList<(string Name, string DefaultBranch)> repositories)
        => repositories.Count == 0
            ? [new DashboardRow(EmptyHint)]
            : repositories.Select(r => new DashboardRow($"{r.Name}   [{r.DefaultBranch}]")).ToList();
}
```

- [ ] **Step 2: Add a test for it**

`tests/Fleet.Tests/Features/Dashboard/ShowDashboard/DashboardRowsTests.cs`:

```csharp
using Fleet.Features.Dashboard.ShowDashboard;

public class DashboardRowsTests
{
    [Fact]
    public void An_empty_list_becomes_a_single_hint_row()
    {
        var rows = DashboardRows.ForRepositories([]);
        Assert.Equal(DashboardRows.EmptyHint, Assert.Single(rows).Text);
    }

    [Fact]
    public void Each_repository_becomes_a_row_with_its_default_branch()
    {
        var rows = DashboardRows.ForRepositories([("widgets", "main"), ("api", "develop")]);

        Assert.Equal(["widgets   [main]", "api   [develop]"], rows.Select(r => r.Text));
    }
}
```

Run: `dotnet test --filter DashboardRowsTests`
Expected: PASS, 2 tests

- [ ] **Step 3: Implement the view**

The view takes callbacks rather than other slices' handlers, which is what keeps it inside the boundary rule. `Program.cs` supplies them.

```csharp
using System.Collections.ObjectModel;
using Terminal.Gui;

namespace Fleet.Features.Dashboard.ShowDashboard;

public sealed record DashboardCallbacks(
    Func<Task<IReadOnlyList<(string Name, string DefaultBranch)>>> LoadRepositories,
    Func<Task<string?>> AddRepository);

public static class ShowDashboardView
{
    /// <param name="callbacks">
    /// AddRepository returns null on success, or an error message to show. Supplied
    /// by Program.cs, so this view depends on no other slice.
    /// </param>
    public static void Show(string projectName, DashboardCallbacks callbacks)
    {
        Application.Init();

        var window = new Window
        {
            Title = $"fleet — {projectName}",
            BorderStyle = LineStyle.Rounded,
        };

        var repoFrame = new FrameView
        {
            Title = "Repositories", X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Percent(45),
        };
        var repoList = new ListView { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
        repoFrame.Add(repoList);

        var agentFrame = new FrameView
        {
            Title = "Agents", X = 0, Y = Pos.Bottom(repoFrame), Width = Dim.Fill(), Height = Dim.Fill(2),
        };
        agentFrame.Add(new Label
        {
            X = 1, Y = 1, Text = "No agents yet — spawning agents arrives in phase 2.",
        });

        var addButton = new Button { X = 0, Y = Pos.AnchorEnd(2), Text = "_Add repository" };
        var hint = new Label { X = 20, Y = Pos.AnchorEnd(2), Text = "a add    r refresh    q quit" };

        async Task RefreshAsync()
        {
            var repositories = await callbacks.LoadRepositories();
            var rows = DashboardRows.ForRepositories(repositories).Select(r => r.Text).ToList();
            repoList.SetSource(new ObservableCollection<string>(rows));
        }

        async Task AddAsync()
        {
            var error = await callbacks.AddRepository();

            // Repository creation is destructive-adjacent, so failure is loud.
            if (error is not null) MessageBox.ErrorQuery("Could not add repository", error, "OK");

            await RefreshAsync();
        }

        addButton.Accepting += (_, e) => { e.Cancel = true; _ = AddAsync(); };

        window.KeyDown += (_, key) =>
        {
            if (key == Key.Q) { Application.RequestStop(window); key.Handled = true; }
            else if (key == Key.A) { _ = AddAsync(); key.Handled = true; }
            else if (key == Key.R) { _ = RefreshAsync(); key.Handled = true; }
        };

        window.Add(repoFrame, agentFrame, addButton, hint);
        _ = RefreshAsync();

        try { Application.Run(window); }
        finally { window.Dispose(); Application.Shutdown(); }
    }
}
```

- [ ] **Step 4: Verify boundaries**

Run: `dotnet test --filter SliceBoundaryTests`
Expected: PASS — the dashboard names no other slice.

- [ ] **Step 5: Commit**

```powershell
git add src/Fleet/Features/Dashboard tests/Fleet.Tests/Features/Dashboard
git commit -m "feat(ShowDashboard): repository and agent panes, add wired to button and keybind"
```

---

### Task 19: `Program.cs` — the composition root

The only file allowed to name a `Platform` type, and the only place slices are wired to each other.

**Files:**
- Create: `src/Fleet/Program.cs`

- [ ] **Step 1: Implement**

```csharp
using Fleet.Features.Dashboard.ShowDashboard;
using Fleet.Features.Diagnostics.RunDoctor;
using Fleet.Features.Projects.CreateProject;
using Fleet.Features.Projects.OpenProject;
using Fleet.Features.Projects.PickProject;
using Fleet.Features.Repositories.AddRepository;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Platform.Git;
using Fleet.Platform.Logging;
using Fleet.Platform.Mux;
using Fleet.Platform.Mux.WezTerm;
using Fleet.Platform.Storage;
using Fleet.Ports;
using Fleet.Ports.Git;
using Fleet.Ports.Mux;
using Fleet.Ports.Projects;

namespace Fleet;

public static class Program
{
    public static async Task<int> Main(string[] args) => (args.Length > 0 ? args[0] : "") switch
    {
        ""       => await PickAndOpenAsync(),
        "dash"   => await DashAsync(args[1..]),
        "doctor" => await DoctorAsync(),
        "--help" or "-h" or "help" => Help(),
        var verb => Unknown(verb),
    };

    // --- composition ------------------------------------------------------

    private static IFleetLog NewLog() => new FileLog();

    private static IProjectStore NewProjectStore() => new JsonProjectStore();

    private static IGitRunner NewGit() => new GitRunner();

    /// <summary>
    /// Resolves the driver and wraps it, so no slice ever sees a raw one. Phase 1
    /// implements wezterm only; tmux and embedded report as unsupported.
    /// </summary>
    private static IMuxDriver NewMux(IFleetLog log, out string chosen, out string? unsupported)
    {
        chosen = DriverSelector.Choose(MuxEnvironment.Current(MuxEnvironment.OnPath));

        unsupported = chosen == DriverSelector.WezTerm
            ? null
            : $"the '{chosen}' driver is not implemented yet (phase 1 ships wezterm only)";

        return new FailSilentDriver(new WezTermDriver(), log.Swallowed);
    }

    // --- commands ---------------------------------------------------------

    private static async Task<int> PickAndOpenAsync()
    {
        var log = NewLog();
        var store = NewProjectStore();

        var creator = new CreateProjectHandler(store);

        // The picker is decoupled from CreateProject: it asks for a project by
        // callback, and only this file knows both slices exist.
        var project = PickProjectView.Show(
            new PickProjectHandler(store),
            createProject: () =>
            {
                CreateProjectView.Show(creator, out var created);
                return created;
            });

        if (project is null) return 0;   // quitting is not a failure

        var mux = NewMux(log, out _, out var unsupported);
        if (unsupported is not null)
        {
            Console.Error.WriteLine($"fleet: {unsupported}");
            return 1;
        }

        var result = await new OpenProjectHandler(mux).HandleAsync(new OpenProjectCommand(
            project,
            Harness: "claude",
            // Must be this binary, not the string "fleet": during development
            // nothing named fleet resolves on PATH.
            FleetExecutable: Environment.ProcessPath ?? "fleet"));

        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"fleet: {result.Error}");
            return 1;
        }

        return 0;
    }

    private static Task<int> DashAsync(string[] args)
    {
        var name = ValueOf(args, "--project");
        if (string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("fleet dash: --project <name> is required");
            return Task.FromResult(2);
        }

        var project = NewProjectStore().Load(name);
        if (project is null)
        {
            Console.Error.WriteLine($"fleet dash: no saved project '{name}'");
            return Task.FromResult(1);
        }

        var git = NewGit();
        var lister = new ListRepositoriesHandler(git);
        var adder = new AddRepositoryHandler(git);

        ShowDashboardView.Show(project.Name, new DashboardCallbacks(
            LoadRepositories: async () =>
                (await lister.HandleAsync(project.Root))
                    .Select(r => (r.Name, r.DefaultBranch))
                    .ToList(),

            AddRepository: async () =>
            {
                var request = AddRepositoryView.Show(project.Root);
                if (request is null) return null;   // cancelled

                var outcome = await adder.HandleAsync(request);
                return outcome.Succeeded ? null : outcome.Error;
            }));

        return Task.FromResult(0);
    }

    private static async Task<int> DoctorAsync()
    {
        var log = NewLog();
        var mux = NewMux(log, out var chosen, out var unsupported);
        var git = NewGit();

        var handler = new RunDoctorHandler(mux, NewProjectStore(), log, async () =>
        {
            var r = await git.RunAsync(Environment.CurrentDirectory, ["--version"]);
            return r.Ok ? r.Out : null;
        });

        var report = await handler.HandleAsync(new RunDoctorCommand(chosen, unsupported));

        Console.WriteLine("fleet doctor");
        Console.WriteLine($"  config        {FleetPaths.Config}");
        Console.WriteLine($"  mux driver    {report.ChosenDriver}");
        Console.WriteLine($"  mux reachable {(report.MuxReachable ? "yes" : "no")}");
        Console.WriteLine($"  git           {report.GitVersion ?? "NOT FOUND"}");
        Console.WriteLine($"  projects      {report.Projects.Count}");
        foreach (var p in report.Projects) Console.WriteLine($"                {p.Name} -> {p.Root}");

        if (report.RecentSwallowed.Count > 0)
        {
            Console.WriteLine("  recent swallowed failures:");
            foreach (var line in report.RecentSwallowed) Console.WriteLine($"                {line}");
        }

        foreach (var problem in report.Problems) Console.WriteLine($"  ! {problem}");

        Console.WriteLine(report.Healthy ? "OK" : $"{report.Problems.Count} problem(s)");
        return report.Healthy ? 0 : 1;
    }

    // --- helpers ----------------------------------------------------------

    private static string? ValueOf(string[] args, string flag)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static int Help()
    {
        Console.WriteLine("""
            fleet — orchestration for AI coding agents

            usage:
              fleet                       pick a project and open it
              fleet dash --project <name> the dashboard (runs inside a pane)
              fleet doctor                check the environment
            """);
        return 0;
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'. Try 'fleet --help'.");
        return 2;
    }
}
```

- [ ] **Step 2: Build and run the whole suite**

Run: `dotnet build && dotnet test`
Expected: `Build succeeded. 0 Warning(s)`, all tests pass — including all four `SliceBoundaryTests`.

- [ ] **Step 3: Walk the spec end to end**

Run: `dotnet run --project src/Fleet`

1. Picker appears listing saved projects and `＋  New project…`
2. Press `n`, enter a name and an existing directory, press Create
3. A WezTerm window opens at that root, split in two — Claude left, dashboard right, focus right
4. Press `a` in the dashboard, create a new repository `widgets` with default branch `main`
5. The repository list shows `widgets   [main]`
6. Press `a` again with the same name — an error box appears rather than a silent failure

Verify point 5 on disk:

```powershell
git -C <root>\widgets rev-parse --is-bare-repository     # true
git -C <root>\widgets\main rev-parse --abbrev-ref HEAD   # main
```

- [ ] **Step 4: Commit**

```powershell
git add src/Fleet/Program.cs
git commit -m "feat: composition root wiring every slice"
```

---

### Task 20: Phase 1 wrap-up

- [ ] **Step 1: Confirm CI is green on both runners**

Push and check `build` and `aot-smoke` on `windows-latest` and `ubuntu-latest`. The Linux `aot-smoke` result answers the outstanding "Terminal.Gui v2 AOT on a real Linux runner" item from `DESIGN.md`.

- [ ] **Step 2: Update `DESIGN.md`**

Two edits:
- Replace the **Proposed layout** section with the vertical-slice layout and the architecture rules from this plan.
- Record the AOT outcomes in the **Verification log**, and remove anything now settled from **Still to verify**.

- [ ] **Step 3: Write the README**

`README.md`: what fleet is, the phase 1 feature set, build and run instructions, the architecture in a paragraph, and the fact that only the WezTerm driver ships so far.

- [ ] **Step 4: Commit and tag**

```powershell
git add README.md docs/DESIGN.md
git commit -m "docs: phase 1 complete, layout and verification recorded"
git tag phase-1
```

---

## Not in phase 1

Deliberate deferrals, each with a home in `docs/DESIGN.md`: spawning agents (`new`, `send`, `reap`, `restore`), the hook reporter and agent state storage, the `tmux` and `embedded` drivers, the plain and worktree-container repo layouts, adopting an existing local directory as a repository, cost tracking, the write guard, and the orchestrator.

## Open questions for phase 2

- **`IAgentStateStore` implementation.** `DESIGN.md` defers daemonless-files versus a state daemon until the WezTerm driver works. It now does, so this is the first phase 2 decision.
- **Where does the harness name come from?** `Program.cs` hardcodes `"claude"`. `harness.d/*.toml` is the eventual answer, along with the still-open Tomlyn-versus-JSON question.
- **Does the dashboard survive a WezTerm restart?** Projects are on disk, but nothing rebuilds panes yet. That is `restore`.
- **Does `Platform` want its own project?** If the architecture tests ever prove insufficient, splitting it out restores compile-time enforcement mechanically.
