using Fleet.Features.Menu.ShowMenu;
using Fleet.Ports.Settings;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Tests.Features.Menu;

public class ToggleSettingHandlerTests
{
    private sealed class FakeSettings(SettingsConfig stored) : ISettingsStore
    {
        public SettingsConfig Stored { get; private set; } = stored;

        public string? SavedFor { get; private set; }

        public SettingsConfig Load(string project) => Stored;

        public void Save(string project, SettingsConfig config)
        {
            SavedFor = project;
            Stored = config;
        }
    }

    private sealed class Recorder
    {
        public List<SettingsConfig> Shared { get; } = [];

        public List<(string Title, string Body)> Toasts { get; } = [];
    }

    private static (ToggleSettingHandler Handler, FakeSettings Settings, Recorder Seen) Build(SettingsConfig stored)
    {
        var settings = new FakeSettings(stored);
        var seen = new Recorder();
        var handler = new ToggleSettingHandler(
            settings,
            seen.Shared.Add,
            (title, body) => seen.Toasts.Add((title, body)));

        return (handler, settings, seen);
    }

    [Fact]
    public void Flip_saves_the_flipped_settings_for_the_project_and_the_shared_ones()
    {
        var (handler, settings, seen) = Build(SettingsConfig.Default.WithShowMenuKeys(true));

        var next = handler.Flip("techweb", FleetAction.EditShowMenuKeys, settings.Stored);

        Assert.False(next.ShowMenuKeys);
        Assert.Equal("techweb", settings.SavedFor);
        Assert.False(settings.Stored.ShowMenuKeys);
        Assert.False(Assert.Single(seen.Shared).ShowMenuKeys);
        Assert.Empty(seen.Toasts);
    }

    [Fact]
    public void Toggle_loads_flips_saves_and_toasts_the_new_state()
    {
        var (handler, settings, seen) = Build(SettingsConfig.Default.WithButtonHints(ButtonHints.Text));

        var said = handler.Toggle("techweb", FleetAction.EditButtonHints);

        Assert.Equal("button hints: tooltips", said);
        Assert.Equal(ButtonHints.Tooltips, settings.Stored.ButtonHints);
        Assert.Equal(ButtonHints.Tooltips, Assert.Single(seen.Shared).ButtonHints);
        Assert.Equal(("fleet · techweb", "button hints: tooltips"), Assert.Single(seen.Toasts));
    }

    [Fact]
    public void Toggling_twice_goes_back_to_where_it_started()
    {
        var (handler, settings, _) = Build(SettingsConfig.Default.WithMainOrchestratorInNvim(true));

        handler.Toggle("techweb", FleetAction.EditMainOrchestratorInNvim);
        Assert.False(settings.Stored.MainOrchestratorInNvim);

        handler.Toggle("techweb", FleetAction.EditMainOrchestratorInNvim);
        Assert.True(settings.Stored.MainOrchestratorInNvim);
    }

    [Theory]
    [InlineData(FleetAction.EditShowMenuKeys, "show keybinds: off")]
    [InlineData(FleetAction.EditButtonHints, "button hints: tooltips")]
    [InlineData(FleetAction.EditMainOrchestratorInNvim, "main orchestrator in nvim: off")]
    [InlineData(FleetAction.EditSubOrchestratorsInNvim, "sub-orchestrators in nvim: off")]
    [InlineData(FleetAction.EditNvimConfig, "nvim config: user")]
    public void Every_toggle_says_its_new_state(FleetAction action, string expected)
    {
        var start = SettingsConfig.Default
            .WithShowMenuKeys(true)
            .WithButtonHints(ButtonHints.Text)
            .WithMainOrchestratorInNvim(true)
            .WithSubOrchestratorsInNvim(true)
            .WithNvim(NvimConfig.Fleet);
        var (handler, _, _) = Build(start);

        Assert.Equal(expected, handler.Toggle("techweb", action));
    }

    [Fact]
    public void Every_menu_toggle_is_handled()
    {
        var toggles = Enum.GetValues<FleetAction>().Where(FleetMenus.IsToggle).ToList();

        Assert.Equal(5, toggles.Count);

        foreach (var action in toggles)
        {
            var (handler, settings, _) = Build(SettingsConfig.Default);

            handler.Toggle("techweb", action);

            Assert.NotEqual(SettingsConfig.Default, settings.Stored);
        }
    }
}
