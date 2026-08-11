namespace Fleet.Ui.Constants;

public static class FleetGlyphs
{
    public const string Branch = "";

    public const string Ahead = "↑";

    public const string Behind = "↓";

    public const string Dirty = "●";

    public const string Ship = "";

    public const string PillLeft = "";

    public const string PillRight = "";

    public static readonly string[] Spinner =
        ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public static string Frame(int tick) => Spinner[Math.Abs(tick) % Spinner.Length];
}
