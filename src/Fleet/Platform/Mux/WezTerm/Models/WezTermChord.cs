namespace Fleet.Platform.Mux.WezTerm.Models;

public sealed record WezTermChord(string Key, string Mods)
{
    public static WezTermChord From(string keyText)
    {
        var parts = keyText.Split('+', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            return new WezTermChord(" ", "CTRL");
        }

        var mods = new List<string>();

        for (var i = 0; i < parts.Length - 1; i++)
        {
            var mod = parts[i].Trim().ToUpperInvariant();

            mods.Add(mod switch
            {
                "CONTROL" => "CTRL",
                _ => mod,
            });
        }

        var key = parts[^1].Trim();

        return new WezTermChord(
            KeyName(key),
            mods.Count == 0 ? "NONE" : string.Join("|", mods));
    }

    private static string KeyName(string key) => key.ToUpperInvariant() switch
    {
        "SPACE" => " ",
        "ESC" => "Escape",
        "ENTER" => "Enter",
        "TAB" => "Tab",
        _ => key.Length == 1 ? key.ToLowerInvariant() : key,
    };
}
