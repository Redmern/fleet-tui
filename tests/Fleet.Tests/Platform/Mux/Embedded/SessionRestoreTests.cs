using System.Text.Json;
using Fleet.Cli.Composition;
using Fleet.Platform.Mux.Embedded.Daemon;
using Fleet.Platform.Mux.Embedded.Model;
using Fleet.Platform.Mux.Embedded.Protocol;
using Fleet.Shared.Constants;

namespace Fleet.Tests.Platform.Mux.Embedded;

public sealed class SessionRestoreTests
{
    private static IReadOnlyList<string> Same(IReadOnlyList<string> args) => args;

    [Fact]
    public void A_snapshot_restores_workspaces_tabs_splits_and_floats()
    {
        var model = new MuxModel();
        var left = model.Spawn("alpha", "C:/a", ["claude"]);
        model.Split(left.Id, sideBySide: true, newFirst: false, 30, "C:/b", ["nvim"]);
        model.SetTitle(left.Id, "api / main");
        model.Spawn("alpha", "C:/c", []);
        var box = model.SpawnFloat("alpha", "C:/d", ["pwsh"], new Rect(5, 3, 40, 12));
        model.SetTitle(box.Id, "notes");
        model.Spawn("beta", "C:/e", ["claude"]);
        left.Env = new Dictionary<string, string> { ["CLAUDE_CODE_FORCE_SESSION_PERSISTENCE"] = "1" };

        var json = JsonSerializer.Serialize(model.Snapshot(), SessionJsonContext.Default.SessionSnapshot);
        var snapshot = JsonSerializer.Deserialize(json, SessionJsonContext.Default.SessionSnapshot)!;

        var restored = new MuxModel();
        var panes = restored.Restore(snapshot, Same);

        Assert.Equal(5, panes.Count);
        Assert.Equal(["alpha", "beta"], restored.ListWorkspaces(null).Select(w => w.Name));

        var alpha = restored.Workspace("alpha")!;
        Assert.Equal(2, alpha.Tabs.Count);
        Assert.Equal(alpha.Tabs[1].Id, alpha.ActiveTab);

        var split = Assert.IsType<LayoutSplit>(alpha.Tabs[0].Root);
        Assert.True(split.SideBySide);
        Assert.Equal(0.7, split.Ratio, 2);
        Assert.Equal("api / main", alpha.Tabs[0].Title);

        var first = restored.Pane(((LayoutLeaf)split.First).Pane)!;
        Assert.Equal("C:/a", first.Cwd);
        Assert.Equal(["claude"], first.Args);
        Assert.Equal("1", first.Env["CLAUDE_CODE_FORCE_SESSION_PERSISTENCE"]);
        Assert.Equal(((LayoutLeaf)split.Second).Pane, alpha.Tabs[0].ActivePane);
        Assert.Equal(["nvim"], restored.Pane(alpha.Tabs[0].ActivePane)!.Args);

        var restoredFloat = Assert.Single(alpha.Floats);
        Assert.Equal(new Rect(5, 3, 40, 12), restoredFloat.Bounds);
        Assert.Equal("notes", restoredFloat.Title);
        Assert.Equal(["pwsh"], restored.Pane(restoredFloat.Pane)!.Args);
        Assert.True(alpha.FloatsShown);
    }

    [Fact]
    public void Modal_floats_and_the_overlay_workspace_are_not_saved()
    {
        var model = new MuxModel();
        model.Spawn("alpha", "C:/a", ["claude"]);
        model.SpawnFloat("alpha", "C:/a", ["fleet", "menu"], modal: true);
        model.Spawn(MuxModel.OverlayWorkspace, "C:/a", ["fleet", "menu"]);

        var snapshot = model.Snapshot();

        var alpha = Assert.Single(snapshot.Workspaces);
        Assert.Equal("alpha", alpha.Name);
        Assert.Empty(alpha.Floats);
        Assert.Equal(["claude"], alpha.Tabs[0].Root.Pane!.Args);
    }

    [Fact]
    public void An_empty_model_saves_no_workspaces()
    {
        var model = new MuxModel();
        var pane = model.Spawn("alpha", "C:/a", []);
        model.Kill(pane.Id);

        Assert.Empty(model.Snapshot().Workspaces);
    }

    [Fact]
    public void A_zoomed_tab_stays_zoomed()
    {
        var model = new MuxModel();
        var left = model.Spawn("alpha", "C:/a", []);
        model.Split(left.Id, sideBySide: true, newFirst: false, 50, "C:/b", []);
        var client = model.Connect(80, 24, "alpha");
        model.Focus(left.Id);
        Assert.True(model.ToggleZoom(client.Id));

        var restored = new MuxModel();
        restored.Restore(model.Snapshot(), Same);

        var tab = restored.Workspace("alpha")!.Tabs[0];
        Assert.Equal(tab.Root.Panes().First(), tab.Zoomed);
    }

    private static string? FocusedAfterRestore(MuxModel model)
    {
        var restored = new MuxModel();
        restored.Restore(model.Snapshot(), Same);
        var client = restored.Connect(100, 30, "alpha");
        var focused = restored.View(client.Id)!.Focused;
        return focused is null ? null : string.Join(' ', restored.Pane(focused)!.Args);
    }

    [Fact]
    public void A_focused_float_is_focused_again_after_a_restore()
    {
        var model = new MuxModel();
        model.Spawn("alpha", "C:/a", ["tile"]);
        model.SpawnFloat("alpha", "C:/a", ["box"]);

        Assert.Equal("box", FocusedAfterRestore(model));
    }

    [Fact]
    public void A_focused_tab_stays_focused_with_its_floats_shown_behind_it()
    {
        var model = new MuxModel();
        var tile = model.Spawn("alpha", "C:/a", ["tile"]);
        model.SpawnFloat("alpha", "C:/a", ["box"]);
        model.Focus(tile.Id);

        Assert.Equal("tile", FocusedAfterRestore(model));
    }

    [Fact]
    public void A_menu_that_had_the_focus_is_not_restored_so_the_tab_gets_it()
    {
        var model = new MuxModel();
        model.Spawn("alpha", "C:/a", ["tile"]);
        model.SpawnFloat("alpha", "C:/a", ["box"]);
        model.SpawnFloat("alpha", "C:/a", ["fleet", "menu"], modal: true);

        Assert.Equal("tile", FocusedAfterRestore(model));
    }

    [Fact]
    public void Relaunch_is_applied_to_every_restored_pane()
    {
        var model = new MuxModel();
        model.Spawn("alpha", "C:/a", ["claude"]);

        var restored = new MuxModel();
        restored.Restore(model.Snapshot(), AgentHarness.Resumed);

        Assert.Equal(["claude", "--continue"], Assert.Single(restored.Panes).Args);
    }

    [Fact]
    public void Resumed_continues_claude_and_the_orchestrator_and_leaves_the_rest()
    {
        Assert.Equal(["claude", AgentHarness.ResumeArgument], AgentHarness.Resumed(["claude"]));
        Assert.Equal(
            AgentHarness.OrchestratorCommand(resume: true),
            AgentHarness.Resumed(AgentHarness.OrchestratorCommand(resume: false)));
        Assert.Contains(
            "ClaudeCode --continue",
            AgentHarness.Resumed(AgentHarness.CommandFor(AgentHarness.Nvim, withClaude: true))[2],
            StringComparison.Ordinal);

        IReadOnlyList<string> resumed = ["claude", "--continue"];
        Assert.Same(resumed, AgentHarness.Resumed(resumed));
        Assert.Equal(AgentHarness.CommandFor(AgentHarness.Nvim), AgentHarness.Resumed(AgentHarness.CommandFor(AgentHarness.Nvim)));
        Assert.Equal(["pwsh"], AgentHarness.Resumed(["pwsh"]));
    }

    [Fact]
    public void The_session_file_is_per_endpoint()
    {
        Assert.Equal("embedded-session.json", Path.GetFileName(EmbeddedWiring.SessionFile(null)));
        Assert.Equal("embedded-session-fleet-inner-7.json", Path.GetFileName(EmbeddedWiring.SessionFile("fleet-inner-7")));
        Assert.Equal("embedded-session--tmp-f-sock.json", Path.GetFileName(EmbeddedWiring.SessionFile("/tmp/f.sock")));
    }

    [Fact]
    public async Task A_restarted_daemon_relaunches_the_saved_panes()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"fleet-session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "embedded-session.json");

        try
        {
            var first = new FakePanes();
            await RunDaemonAsync(first, file, async control =>
            {
                var spawned = await control.RequestAsync(new ControlRequest
                {
                    Op = "spawn",
                    Session = "alpha",
                    NewWindow = true,
                    Cwd = dir,
                    Args = ["claude"],
                    Env = new Dictionary<string, string> { ["A"] = "1", [FleetDaemon.ClientVariable] = "c9" },
                });
                Assert.True(spawned.Ok, spawned.Error);

                var split = await control.RequestAsync(new ControlRequest
                {
                    Op = "split",
                    Pane = spawned.Pane,
                    Direction = "right",
                    Percent = 40,
                    Cwd = dir,
                    Args = ["C:/old/fleet.exe", "dash", "--project", "alpha"],
                });
                Assert.True(split.Ok, split.Error);

                await WaitForAsync(() => Saved(file).Contains("dash", StringComparison.Ordinal));
            });

            Assert.True(File.Exists(file));

            var second = new FakePanes();
            await RunDaemonAsync(second, file, async control =>
            {
                await WaitForAsync(() => second.Started.Count == 2);
                var panes = await control.RequestAsync(new ControlRequest { Op = "list-panes" });
                Assert.All(panes.Panes!, p => Assert.Equal("alpha", p.Session));
            });

            var claude = second.ByProgram("claude")!;
            Assert.Equal(["--continue"], claude.Args);
            Assert.Equal("1", claude.Env["A"]);
            Assert.False(claude.Env.ContainsKey(FleetDaemon.ClientVariable));

            var dash = second.ByProgram("fleet-next")!;
            Assert.Equal(["dash", "--project", "alpha"], dash.Args);
            Assert.True(File.Exists(Path.Combine(dir, "embedded-session.previous.json")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Stopping_the_daemon_forgets_its_projects_so_the_next_start_is_fresh()
    {
        var dir = Directory.CreateTempSubdirectory("fleet-shutdown-").FullName;
        var file = Path.Combine(dir, "embedded-session.json");
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        var endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var panes = new FakePanes();
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = endpoint,
            Pty = panes.NewPty,
            Terminal = panes.NewTerminal,
            FleetExecutable = "fleet",
            SessionFile = file,
            SaveEvery = TimeSpan.FromMilliseconds(50),
        });

        try
        {
            var running = daemon.RunAsync();
            await using (var control = await DaemonTests.TestClient.ConnectAsync(endpoint, ClientRoles.Control, 0, 0, null))
            {
                Assert.True((await control.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "techweb", Cwd = dir, Args = ["claude"] })).Ok);

                // fleetd keeps saving while it runs; the shutdown must remove that save and no later tick may bring it back.
                await WaitForAsync(() => Saved(file).Contains("\"techweb\"", StringComparison.Ordinal));
                Assert.True((await control.RequestAsync(new ControlRequest { Op = "spawn", Workspace = "api", Cwd = dir, Args = ["claude"] })).Ok);
                _ = control.RequestAsync(new ControlRequest { Op = "shutdown" });
                await running.WaitAsync(TimeSpan.FromSeconds(10));
            }

            Assert.False(File.Exists(file));
            await Task.Delay(200);
            Assert.False(File.Exists(file));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static async Task RunDaemonAsync(FakePanes panes, string file, Func<DaemonTests.TestClient, Task> body)
    {
        var name = $"fleet-test-{Guid.NewGuid():N}"[..20];
        var endpoint = new Endpoint(OperatingSystem.IsWindows() ? name : Path.Combine(Path.GetTempPath(), name + ".sock"));
        var daemon = new FleetDaemon(new DaemonOptions
        {
            Endpoint = endpoint,
            Pty = panes.NewPty,
            Terminal = panes.NewTerminal,
            FleetExecutable = "fleet-next",
            SessionFile = file,
            SaveEvery = TimeSpan.FromMilliseconds(50),
        });

        using var stop = new CancellationTokenSource();
        var running = daemon.RunAsync(stop.Token);

        await using (var control = await DaemonTests.TestClient.ConnectAsync(endpoint, ClientRoles.Control, 0, 0, null))
        {
            await body(control);
        }

        await stop.CancelAsync();
        await running;
    }

    // The daemon replaces the file with a move, so a read can meet it mid-swap on Windows.
    private static string Saved(string file)
    {
        try
        {
            return File.Exists(file) ? File.ReadAllText(file) : string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(50);
        }

        Assert.True(condition());
    }
}
