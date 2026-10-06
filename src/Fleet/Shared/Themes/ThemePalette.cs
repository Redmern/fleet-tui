namespace Fleet.Shared.Themes;

public sealed record ThemePalette(
    string Name,
    string Title,
    bool Light,
    string Crust,
    string Mantle,
    string Base,
    string Surface0,
    string Surface1,
    string Overlay0,
    string Subtext0,
    string Text,
    string Blue,
    string Lavender,
    string Green,
    string Yellow,
    string Red,
    string Cursor,
    IReadOnlyList<string> Ansi,
    IReadOnlyList<string> Brights)
{
    public const int AnsiCount = 8;

    public static IReadOnlyList<string> RoleNames { get; } =
    [
        "crust", "mantle", "base", "surface0", "surface1", "overlay0", "subtext0", "text",
        "blue", "lavender", "green", "yellow", "red", "cursor",
    ];

    public string Role(string role) => role switch
    {
        "crust" => Crust,
        "mantle" => Mantle,
        "base" => Base,
        "surface0" => Surface0,
        "surface1" => Surface1,
        "overlay0" => Overlay0,
        "subtext0" => Subtext0,
        "text" => Text,
        "blue" => Blue,
        "lavender" => Lavender,
        "green" => Green,
        "yellow" => Yellow,
        "red" => Red,
        "cursor" => Cursor,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "not a palette role"),
    };

    public ThemePalette WithRole(string role, string color) => role switch
    {
        "crust" => this with { Crust = color },
        "mantle" => this with { Mantle = color },
        "base" => this with { Base = color },
        "surface0" => this with { Surface0 = color },
        "surface1" => this with { Surface1 = color },
        "overlay0" => this with { Overlay0 = color },
        "subtext0" => this with { Subtext0 = color },
        "text" => this with { Text = color },
        "blue" => this with { Blue = color },
        "lavender" => this with { Lavender = color },
        "green" => this with { Green = color },
        "yellow" => this with { Yellow = color },
        "red" => this with { Red = color },
        "cursor" => this with { Cursor = color },
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "not a palette role"),
    };
}
