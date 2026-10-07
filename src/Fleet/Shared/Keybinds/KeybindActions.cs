namespace Fleet.Shared.Keybinds;

public static class KeybindActions
{
    public const string Prefix = "prefix";

    public static string FromMux(string command)
    {
        var trimmed = command.Trim();
        var space = trimmed.IndexOf(' ');

        if (space < 0)
        {
            return trimmed;
        }

        var (verb, arg) = (trimmed[..space], trimmed[(space + 1)..].Trim());

        return verb switch
        {
            "smart-focus" => $"focus-{arg}",
            "resize" => $"resize-{arg}",
            _ => trimmed,
        };
    }
}
