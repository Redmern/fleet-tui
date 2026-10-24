using System.Globalization;
using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Results;
using Fleet.Shared.Themes;
using Fleet.Ui.Constants;
using Fleet.Ui.Models;
using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace Fleet.Ui;

public static class FleetThemeEditor
{
    public const string Roles = "Roles";

    public const string Ansi = "ANSI";

    public const string Brights = "Brights";

    public static readonly Key Save = Key.S;

    public static readonly Key SaveAndUse = Key.U;

    private const string ColorPrefix = "color";

    private static readonly string[] AnsiNames = ["black", "red", "green", "yellow", "blue", "magenta", "cyan", "white"];

    public static IReadOnlyList<ThemeColor> Colors { get; } =
    [
        .. ThemePalette.RoleNames.Select(r => new ThemeColor(Roles, r, r)),
        .. AnsiNames.Select((n, i) => new ThemeColor(Ansi, $"{ColorPrefix}{i}", n)),
        .. AnsiNames.Select((n, i) => new ThemeColor(Brights, $"{ColorPrefix}{i + ThemePalette.AnsiCount}", $"bright {n}")),
    ];

    public static string Of(ThemePalette palette, string key) =>
        Ansi16(key) is { } i
            ? i < ThemePalette.AnsiCount ? palette.Ansi[i] : palette.Brights[i - ThemePalette.AnsiCount]
            : palette.Role(key);

    public static ThemePalette With(ThemePalette palette, string key, string hex)
    {
        if (Ansi16(key) is not { } i)
        {
            return palette.WithRole(key, hex);
        }

        var ansi = palette.Ansi.ToArray();
        var brights = palette.Brights.ToArray();

        if (i < ThemePalette.AnsiCount)
        {
            ansi[i] = hex;
        }
        else
        {
            brights[i - ThemePalette.AnsiCount] = hex;
        }

        return palette with { Ansi = ansi, Brights = brights };
    }

    public static bool Changed(ThemePalette before, ThemePalette after) =>
        Colors.Any(c => HexColor.Normalize(Of(before, c.Key)) != HexColor.Normalize(Of(after, c.Key)));

    public static IReadOnlyList<FleetRow> Rows(ThemePalette palette)
    {
        var width = Colors.Max(c => c.Label.Length);

        return
        [
            .. Colors.Select(c => new FleetRow(
                [
                    new FleetSpan("███ ", FleetTones.Swatch(Of(palette, c.Key))),
                    FleetSpan.Plain(c.Label.PadRight(width)),
                ],
                [FleetSpan.Muted($"{Of(palette, c.Key)} ")])),
        ];
    }

    public static IReadOnlyDictionary<int, FleetRow> Headers() =>
        Colors
            .Select((c, i) => (c.Group, Index: i))
            .Where(c => c.Index == 0 || Colors[c.Index - 1].Group != c.Group)
            .ToDictionary(c => c.Index, c => new FleetRow([new FleetSpan(c.Group, FleetTones.Title)]));

    public static Result<string> NewName(string typed)
    {
        var name = ThemeNames.Normalize(typed);

        if (name.Length == 0)
        {
            return Result<string>.Fail("a theme needs a name with at least one letter or digit");
        }

        if (BuiltInThemes.Find(name) is not null)
        {
            return Result<string>.Fail($"'{name}' is a built-in theme; pick another name");
        }

        return name == OmarchyTheme.CustomName
            ? Result<string>.Fail($"'{name}' is kept for omarchy theme sync; pick another name")
            : Result<string>.Ok(name);
    }

    public static ThemeEdit? Show(IApplication app, Keymap keymap, string title, ThemePalette palette)
    {
        var edited = palette;
        ThemeEdit? result = null;

        var window = FleetTheme.Overlay(title, 60, Colors.Count + Headers().Count + 4 + FleetCorners.Rows);
        var list = FleetTheme.Rows(1, 1, Dim.Fill(2));
        FleetKeys.ApplyMotions(list, keymap);

        void Fill(int item) => FleetRows.Fill(list, Rows(edited), item, headersBefore: Headers());

        void Pick()
        {
            var item = FleetRows.Selected(list);
            var color = Colors[item];

            if (FleetColorDialog.Show(app, $"{color.Label} — {edited.Title}", Of(edited, color.Key)) is { } hex)
            {
                edited = With(edited, color.Key, hex);
            }

            Fill(item);
        }

        void Finish(bool use)
        {
            result = new ThemeEdit(edited, use);
            app.RequestStop(window);
        }

        void Leave()
        {
            FleetModal.Back();
            app.RequestStop(window);
        }

        list.Accepting += (_, e) =>
        {
            e.Handled = true;
            Pick();
        };

        var bar = new FleetActionBar(Pos.AnchorEnd(1));

        bar.Show(
        [
            ("enter", FleetIcons.Edit, Pick),
            ("s", FleetIcons.Save, () => Finish(use: false)),
            ("u", FleetIcons.SaveAndUse, () => Finish(use: true)),
            ("bksp", FleetIcons.Back, Leave),
        ]);

        var claim = FleetModal.Enter();

        void Keys(object? sender, Key key)
        {
            if (!FleetModal.Owns(claim))
            {
                return;
            }

            if (key == Save)
            {
                Finish(use: false);
            }
            else if (key == SaveAndUse)
            {
                Finish(use: true);
            }
            else if (key == FleetKeys.Cancel)
            {
                app.RequestStop(window);
            }
            else if (FleetKeys.GoesBack(key))
            {
                Leave();
            }
            else
            {
                return;
            }

            key.Handled = true;
        }

        app.Keyboard.KeyDown += Keys;
        window.Add(list, bar.Root);
        FleetCorners.Attach(window, () => app.RequestStop(window));
        Fill(0);

        try
        {
            list.SetFocus();
            app.Run(window);
        }
        finally
        {
            FleetModal.Leave();
            app.Keyboard.KeyDown -= Keys;
            window.Dispose();
        }

        return result;
    }

    private static int? Ansi16(string key) =>
        key.StartsWith(ColorPrefix, StringComparison.Ordinal) && int.TryParse(key[ColorPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var i)
            ? i
            : null;
}
