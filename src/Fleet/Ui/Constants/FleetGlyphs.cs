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


    public const string MoreLeft = "‹";

    public const string MoreRight = "›";

    public const string Hidden = "";

    public const string Orchestrator = "";

    public const string Child = "└─";

    public const string Working = "";

    public const string Waiting = "";

    public const string Idle = "";

    public const string Stalled = "";

    public const string Done = "";

    public const string Failed = "";

    public static readonly string[] Spinner =
        ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];

    public static string Frame(int tick) => Spinner[Math.Abs(tick) % Spinner.Length];
}
