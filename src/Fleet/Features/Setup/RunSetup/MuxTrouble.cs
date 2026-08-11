namespace Fleet.Features.Setup.RunSetup;

public static class MuxTrouble
{
    public const string WezTerm = "wezterm";

    public static string? With(string chosen, bool wezTermOnPath)
    {
        if (chosen == WezTerm)
        {
            return null;
        }

        return wezTermOnPath
            ? $"the '{chosen}' driver is not implemented yet; fleet ships wezterm only"
            : "wezterm is not installed, or not on PATH. Install it and run 'fleet setup'.";
    }
}
