using Fleet.Shared.Themes;
using Fleet.Ui.Models;

namespace Fleet.Ui;

public sealed class FleetThemeTabs
{
    public const int Dark = 0;

    public const int Light = 1;

    public const int Custom = 2;

    public const string ActiveMark = "  (active)";

    public const string NewLabel = "New custom theme…";

    private static readonly string[] Titles = ["Dark", "Light", "Custom"];

    private readonly IReadOnlyList<IReadOnlyList<ThemePalette>> themes;

    private FleetThemeTabs(
        IReadOnlyList<IReadOnlyList<ThemePalette>> themes,
        IReadOnlyList<(string Title, IReadOnlyList<PickerEntry> Entries)> tabs,
        int startTab,
        int startIndex)
    {
        this.themes = themes;
        Tabs = tabs;
        StartTab = startTab;
        StartIndex = startIndex;
    }

    public IReadOnlyList<(string Title, IReadOnlyList<PickerEntry> Entries)> Tabs { get; }

    public int StartTab { get; }

    public int StartIndex { get; }

    public static FleetThemeTabs For(
        IReadOnlyList<ThemePalette> all, Func<ThemePalette, bool> isCustom, string active, bool withNew = false, string? select = null)
    {
        IReadOnlyList<ThemePalette>[] themes =
        [
            [.. all.Where(t => !isCustom(t) && !t.Light)],
            [.. all.Where(t => !isCustom(t) && t.Light)],
            [.. all.Where(isCustom)],
        ];

        var tabs = themes
            .Select((list, i) => (Titles[i], (IReadOnlyList<PickerEntry>)
            [
                .. list.Select(t => Entry(t, active)),
                .. withNew && i == Custom ? [new PickerEntry(NewLabel, Key: "n")] : Array.Empty<PickerEntry>(),
            ]))
            .ToList();

        var start = select ?? active;
        var startTab = Array.FindIndex(themes, list => list.Any(t => t.Name == start));

        return startTab < 0
            ? new FleetThemeTabs(themes, tabs, Dark, 0)
            : new FleetThemeTabs(themes, tabs, startTab, themes[startTab].ToList().FindIndex(t => t.Name == start));
    }

    public ThemePalette Pick(int tab, int index) => themes[tab][index];

    public bool IsNew(int tab, int index) => index >= themes[tab].Count && index < Tabs[tab].Entries.Count;

    private static PickerEntry Entry(ThemePalette theme, string active) =>
        new(theme.Title, theme.Name == active ? theme.Name + ActiveMark : theme.Name);
}
