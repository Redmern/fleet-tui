using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;

namespace Fleet.Tests.Features.Menu;

// What a menu started for a view of a remote runs on the machine you sit at, without a project.
public class ThisMachineSessionTests
{
    private sealed class Screens : IThisMachineScreens
    {
        private readonly Queue<FleetAction> _picks = new();

        public List<string> Opened { get; } = [];

        public List<MenuScreen> Settings { get; } = [];

        public Screens Picks(params FleetAction[] picks)
        {
            foreach (var pick in picks)
            {
                _picks.Enqueue(pick);
            }

            return this;
        }

        private Task Open(string screen)
        {
            Opened.Add(screen);
            return Task.CompletedTask;
        }

        public Task SwitchProjectAsync() => Open("switch project");

        public Task NotificationsAsync() => Open("notifications");

        public Task RemotesAsync() => Open("remote machines");

        public void SaveSession() => Open("save session");

        public void Keybinds() => Open("keybinds");

        public void Theme() => Open("theme");

        public void Toggle(FleetAction toggle) => Open($"toggle {toggle}");

        public void Log() => Open("log");

        public void Update() => Open("update");

        public void Version() => Open("version");

        public void WhatsNew() => Open("what's new");

        public FleetAction ShowSettings(MenuScreen screen)
        {
            Settings.Add(screen);
            return _picks.Count > 0 ? _picks.Dequeue() : FleetAction.None;
        }
    }

    public static TheoryData<FleetAction, string> Rows { get; } = new()
    {
        { FleetAction.SwitchProject, "switch project" },
        { FleetAction.Notifications, "notifications" },
        { FleetAction.Remotes, "remote machines" },
        { FleetAction.SaveSession, "save session" },
        { FleetAction.EditKeybinds, "keybinds" },
        { FleetAction.EditTheme, "theme" },
        { FleetAction.EditShowMenuKeys, "toggle EditShowMenuKeys" },
        { FleetAction.EditButtonHints, "toggle EditButtonHints" },
        { FleetAction.ViewLogs, "log" },
        { FleetAction.UpdateFleet, "update" },
        { FleetAction.ShowVersion, "version" },
        { FleetAction.WhatsNew, "what's new" },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task Each_screen_asked_for_opens_its_own_screen_and_no_other(FleetAction asked, string screen)
    {
        var screens = new Screens();

        var code = await ThisMachineSession.RunAsync(asked, screens, () => false);

        Assert.Equal(0, code);
        Assert.Equal([screen], screens.Opened);
        Assert.Empty(screens.Settings);
    }

    [Fact]
    public void Every_owned_screen_and_every_row_of_this_machines_settings_has_a_screen_here()
    {
        Assert.All(ViewerScreens.Owned, a => Assert.Contains(a, ThisMachineSession.Screens));
        Assert.All(FleetMenus.Actions(RemoteMenus.ThisMachine), a => Assert.Contains(a, ThisMachineSession.Screens));
        Assert.Equal(Rows.Count + 1, ThisMachineSession.Screens.Count);
    }

    [Fact]
    public async Task This_machines_settings_shows_its_own_ten_rows_not_the_settings_of_a_project()
    {
        var screens = new Screens();

        await ThisMachineSession.RunAsync(FleetAction.ThisMachineSettings, screens, () => false);

        var shown = Assert.Single(screens.Settings);
        Assert.Same(RemoteMenus.ThisMachineScreen, shown);
        Assert.Equal(10, FleetMenus.Actions(shown.Sections).Count);
        Assert.NotEqual(FleetMenus.Actions(FleetMenus.Settings), FleetMenus.Actions(shown.Sections));
        Assert.Equal("fleet menu › this machine", shown.Title);
        Assert.Empty(screens.Opened);
    }

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_row_picked_on_this_machines_settings_opens_its_own_screen(FleetAction row, string screen)
    {
        var screens = new Screens().Picks(row);

        await ThisMachineSession.RunAsync(FleetAction.ThisMachineSettings, screens, () => false);

        Assert.Equal([screen], screens.Opened);
        Assert.Single(screens.Settings);
    }

    [Fact]
    public async Task Back_from_a_screen_opened_from_this_machines_settings_returns_to_those_settings()
    {
        var screens = new Screens().Picks(FleetAction.ViewLogs, FleetAction.Remotes);

        await ThisMachineSession.RunAsync(FleetAction.ThisMachineSettings, screens, () => true);

        Assert.Equal(["log", "remote machines"], screens.Opened);
        Assert.Equal(3, screens.Settings.Count);
    }

    [Fact]
    public async Task A_screen_closed_without_going_back_ends_the_session()
    {
        var screens = new Screens().Picks(FleetAction.ViewLogs, FleetAction.Remotes);

        await ThisMachineSession.RunAsync(FleetAction.ThisMachineSettings, screens, () => false);

        Assert.Equal(["log"], screens.Opened);
        Assert.Single(screens.Settings);
    }

    [Theory]
    [InlineData(FleetAction.Remotes)]
    [InlineData(FleetAction.SaveSession)]
    [InlineData(FleetAction.SwitchProject)]
    public async Task Back_from_a_screen_that_was_asked_for_directly_closes_and_does_not_open_this_machines_settings(FleetAction asked)
    {
        var screens = new Screens();

        await ThisMachineSession.RunAsync(asked, screens, () => true);

        Assert.Single(screens.Opened);
        Assert.Empty(screens.Settings);
    }
}
