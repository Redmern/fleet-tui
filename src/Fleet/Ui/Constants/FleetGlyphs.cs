namespace Fleet.Ui.Constants;

public static class FleetGlyphs
{
    public const string Branch = "";

    public static readonly string[] Spinner =
        ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public static string Frame(int tick) => Spinner[Math.Abs(tick) % Spinner.Length];
}
