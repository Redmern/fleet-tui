using Fleet.Features.Menu.ShowMenu;
using Fleet.Features.Menu.ShowMenu.Models;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Viewing;
using Fleet.Shared.Viewing.Enums;
using Fleet.Shared.Viewing.Models;

namespace Fleet.Tests.Features.Menu;

// The loop of the `fleet menu` process, driven without Terminal.Gui: a script says what the
// user picks on each screen that is shown, and the test reads what was shown, run and handed back.
public class MenuSessionTests
{
    private static readonly ViewedFrom View = new(ViewKind.View, "homelab");

    private static readonly ViewedFrom Ssh = new(ViewKind.Ssh, "homelab-box");

    private sealed class Menu
    {
        private readonly Queue<Func<MenuScreen, FleetAction>> _picks = new();

        public Menu(ViewedFrom? viewer, IReadOnlyList<string>? announced = null)
        {
            Viewer = viewer;
            var takes = announced ?? ViewerScreens.Ids;
            Session = new MenuSession(
                () => Viewer,
                new MenuDispatch(() => Viewer, id =>
                {
                    if (!takes.Contains(id))
                    {
                        return false;
                    }

                    HandedBack.Add(id);
                    return true;
                }),
                screen =>
                {
                    Shown.Add(screen);
                    return Task.FromResult(_picks.Count > 0 ? _picks.Dequeue()(screen) : FleetAction.None);
                },
                () => Task.FromResult(Editor),
                action =>
                {
                    Ran.Add(action);
                    return Task.FromResult(Code);
                },
                () => Back,
                Hints.Add);
        }

        public MenuSession Session { get; }

        public ViewedFrom? Viewer { get; set; }

        public bool Editor { get; set; } = true;

        public bool Back { get; set; }

        public int Code { get; set; }

        public List<MenuScreen> Shown { get; } = [];

        public List<FleetAction> Ran { get; } = [];

        public List<string> HandedBack { get; } = [];

        public List<string> Hints { get; } = [];

        public Menu Picks(params FleetAction[] picks)
        {
            foreach (var pick in picks)
            {
                _picks.Enqueue(_ => pick);
            }

            return this;
        }

        public Menu Then(Func<MenuScreen, FleetAction> step)
        {
            _picks.Enqueue(step);
            return this;
        }
    }

    private static string[] Titles(MenuScreen screen) => [.. screen.Sections.Select(s => s.Header ?? string.Empty)];

    [Fact]
    public async Task A_menu_opened_by_someone_sitting_at_the_machine_shows_the_menu_it_always_showed()
    {
        var menu = new Menu(null);

        var end = await menu.Session.RunAsync(FleetAction.None);

        var shown = Assert.Single(menu.Shown);
        Assert.Same(FleetMenus.Main, shown.Sections);
        Assert.Equal(("fleet menu", (string?)null), (shown.Title, shown.Note));
        Assert.Equal(new MenuEnd(0), end);
    }

    [Fact]
    public async Task A_menu_drawn_for_a_remote_view_shows_the_grouped_screens_of_that_viewer()
    {
        var menu = new Menu(View).Picks(FleetAction.OpenSettings);
        menu.Back = true;

        await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal(["PROJECT on homelab", "MACHINE homelab", "FLEET this machine"], Titles(menu.Shown[0]));
        Assert.Equal(["PROJECT on homelab", "MACHINE homelab"], Titles(menu.Shown[1]));
        Assert.Equal("fleet menu › settings", menu.Shown[1].Title);
        Assert.All(menu.Shown, s => Assert.Null(s.Note));
    }

    [Fact]
    public async Task A_menu_drawn_for_an_ssh_window_shows_two_groups_and_the_note()
    {
        var menu = new Menu(Ssh);

        await menu.Session.RunAsync(FleetAction.None);

        var shown = Assert.Single(menu.Shown);
        Assert.Equal(["PROJECT on homelab-box", "MACHINE homelab-box"], Titles(shown));
        Assert.Equal(ViewerHints.ScreensWhereYouSit, shown.Note);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_editor_row_shows_on_the_main_screen_only_when_this_pane_can_open_one(bool editor)
    {
        var menu = new Menu(View) { Editor = editor };

        await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal(editor, FleetMenus.Actions(menu.Shown[0].Sections).Contains(FleetAction.OpenEditor));
    }

    [Fact]
    public async Task A_project_row_picked_in_a_remote_view_runs_on_the_remote_and_the_menu_ends_with_its_code()
    {
        var menu = new Menu(View) { Code = 3 }.Picks(FleetAction.ListAgents);

        var end = await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal([FleetAction.ListAgents], menu.Ran);
        Assert.Empty(menu.HandedBack);
        Assert.Equal(new MenuEnd(3), end);
    }

    [Fact]
    public async Task A_fleet_row_picked_in_a_remote_view_is_handed_back_and_the_remotes_menu_closes()
    {
        var menu = new Menu(View).Picks(FleetAction.Remotes);

        var end = await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal(["remotes"], menu.HandedBack);
        Assert.Empty(menu.Ran);
        Assert.Single(menu.Shown);
        Assert.Equal(new MenuEnd(0), end);
    }

    [Fact]
    public async Task A_fleet_row_an_older_viewer_cannot_take_shows_the_hint_and_returns_to_the_menu_it_was_picked_on()
    {
        var menu = new Menu(View, ViewerScreens.Legacy).Picks(FleetAction.SaveSession);

        var end = await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal([ViewerHints.ScreensWhereYouSit], menu.Hints);
        Assert.Empty(menu.Ran);
        Assert.Equal([FleetAction.None, FleetAction.None], menu.Shown.Select(s => s.Submenu));
        Assert.Equal(new MenuEnd(0), end);
    }

    [Fact]
    public async Task Back_from_a_screen_returns_to_the_menu_it_was_opened_from_and_back_again_to_the_main_menu()
    {
        var menu = new Menu(View) { Back = true }.Picks(FleetAction.OpenSettings, FleetAction.ViewLogs);

        await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal([FleetAction.ViewLogs], menu.Ran);
        Assert.Equal(
            [FleetAction.None, FleetAction.OpenSettings, FleetAction.OpenSettings, FleetAction.None],
            menu.Shown.Select(s => s.Submenu));
    }

    [Fact]
    public async Task A_screen_closed_without_going_back_ends_the_menu()
    {
        var menu = new Menu(View).Picks(FleetAction.OpenSettings, FleetAction.ViewLogs);

        await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal(2, menu.Shown.Count);
    }

    [Fact]
    public async Task An_action_asked_for_at_the_start_runs_without_a_menu_and_is_not_offered_back_a_second_time()
    {
        var asked = 0;
        var ran = new List<FleetAction>();
        var session = new MenuSession(
            () => null,
            new MenuDispatch(() => null, _ =>
            {
                asked++;
                return false;
            }),
            _ => throw new InvalidOperationException("no menu should show"),
            () => Task.FromResult(true),
            action =>
            {
                ran.Add(action);
                return Task.FromResult(0);
            },
            () => false,
            _ => { });

        await session.RunAsync(FleetAction.SwitchProject);

        Assert.Equal([FleetAction.SwitchProject], ran);
        Assert.Equal(0, asked);
    }

    // Only the action the menu was started for was settled before the menu ran. A row picked
    // later in the same menu must go through the viewer's rules again.
    [Fact]
    public async Task A_fleet_row_picked_after_the_screen_the_menu_was_started_for_is_still_handed_back()
    {
        var menu = new Menu(View) { Back = true };
        menu.Picks(FleetAction.None, FleetAction.Remotes);

        var end = await menu.Session.RunAsync(FleetAction.ViewLogs);

        Assert.Equal([FleetAction.ViewLogs], menu.Ran);
        Assert.Equal(["remotes"], menu.HandedBack);
        Assert.Equal([FleetAction.OpenSettings, FleetAction.None], menu.Shown.Select(s => s.Submenu));
        Assert.Equal(new MenuEnd(0), end);
    }

    [Fact]
    public async Task A_fleet_row_picked_after_the_submenu_the_menu_was_started_on_is_handed_back_and_never_runs_on_the_remote()
    {
        var menu = new Menu(View) { Back = true };
        menu.Picks(FleetAction.None, FleetAction.Remotes);

        var end = await menu.Session.RunAsync(FleetAction.OpenSettings);

        Assert.Empty(menu.Ran);
        Assert.Equal(["remotes"], menu.HandedBack);
        Assert.Equal([FleetAction.OpenSettings, FleetAction.None], menu.Shown.Select(s => s.Submenu));
        Assert.Equal(new MenuEnd(0), end);
    }

    [Fact]
    public async Task A_submenu_asked_for_at_the_start_opens_on_that_screen()
    {
        var menu = new Menu(View);

        await menu.Session.RunAsync(FleetAction.OpenSettings);

        Assert.Equal(FleetAction.OpenSettings, Assert.Single(menu.Shown).Submenu);
    }

    [Theory]
    [InlineData(null, "view:homelab")]
    [InlineData("view:homelab", null)]
    [InlineData("view:homelab", "view:barn")]
    [InlineData("view:homelab", "ssh:homelab")]
    public void A_parked_menu_opened_by_another_viewer_than_it_was_drawn_for_must_stop_and_redraw(string? drawnFor, string? openedBy) =>
        Assert.NotNull(new Menu(null).Session.Opened(null, drawnFor, openedBy));

    [Theory]
    [InlineData(null, null)]
    [InlineData("view:homelab", "view:homelab")]
    [InlineData("ssh:homelab", "ssh:homelab")]
    public void A_parked_menu_opened_plainly_by_the_viewer_it_was_drawn_for_is_left_as_it_is(string? drawnFor, string? openedBy) =>
        Assert.Null(new Menu(null).Session.Opened(null, drawnFor, openedBy));

    [Fact]
    public async Task A_menu_drawn_for_the_machine_and_opened_from_a_remote_view_is_drawn_again_grouped_and_stays_open()
    {
        var menu = new Menu(null);
        menu.Then(_ =>
        {
            menu.Viewer = View;
            menu.Session.Opened(null, null, View.Mark)!();
            return FleetAction.None;
        });

        await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal(2, menu.Shown.Count);
        Assert.Same(FleetMenus.Main, menu.Shown[0].Sections);
        Assert.Equal(["PROJECT on homelab", "MACHINE homelab", "FLEET this machine"], Titles(menu.Shown[1]));
    }

    [Fact]
    public async Task After_a_regroup_closing_the_menu_closes_it()
    {
        var menu = new Menu(null);
        menu.Then(_ =>
        {
            menu.Viewer = View;
            menu.Session.Opened(null, null, View.Mark)!();
            return FleetAction.None;
        });

        var end = await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal(2, menu.Shown.Count);
        Assert.Equal(new MenuEnd(0), end);
    }

    [Fact]
    public async Task A_parked_menu_opened_with_an_action_runs_that_action_through_the_viewers_rules()
    {
        var menu = new Menu(View);
        menu.Then(_ =>
        {
            menu.Session.Opened("remotes", View.Mark, View.Mark)!();
            return FleetAction.None;
        });

        await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal(["remotes"], menu.HandedBack);
        Assert.Empty(menu.Ran);
    }

    [Fact]
    public async Task A_parked_menu_opened_in_an_ssh_window_with_a_fleet_action_hints_and_closes()
    {
        var menu = new Menu(Ssh);
        menu.Then(_ =>
        {
            menu.Session.Opened("head-voice", Ssh.Mark, Ssh.Mark)!();
            return FleetAction.None;
        });

        var end = await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal([ViewerHints.ScreensWhereYouSit], menu.Hints);
        Assert.Single(menu.Shown);
        Assert.Equal(new MenuEnd(0), end);
    }

    [Fact]
    public async Task An_action_that_arrives_with_a_regroup_runs_and_one_close_of_the_main_menu_then_closes_it()
    {
        var menu = new Menu(null) { Back = true };
        menu.Then(_ =>
        {
            menu.Viewer = View;
            menu.Session.Opened("viewlogs", null, View.Mark)!();
            return FleetAction.None;
        });

        await menu.Session.RunAsync(FleetAction.None);

        Assert.Equal([FleetAction.ViewLogs], menu.Ran);
        Assert.Equal(
            [FleetAction.None, FleetAction.OpenSettings, FleetAction.None],
            menu.Shown.Select(s => s.Submenu));
    }

    [Theory]
    [InlineData("open-project", FleetAction.OpenProject)]
    [InlineData("new-project", FleetAction.NewProject)]
    public async Task A_parked_menu_opened_to_pick_a_project_ends_with_that_pick(string action, FleetAction pick)
    {
        var menu = new Menu(null);
        menu.Then(_ =>
        {
            menu.Session.Opened(action, null, null)!();
            return FleetAction.None;
        });

        Assert.Equal(new MenuEnd(0, pick), await menu.Session.RunAsync(FleetAction.None));
        Assert.Empty(menu.Ran);
    }
}
