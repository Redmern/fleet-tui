namespace Fleet.Shared.Settings;

public static class DispatchTrigger
{
    public static bool IsValid(string text) =>
        text.Length == 1
        && !char.IsWhiteSpace(text[0])
        && !char.IsLetterOrDigit(text[0]);

    public static string Normalize(string text)
    {
        var trimmed = text.Trim();

        return IsValid(trimmed) ? trimmed : string.Empty;
    }

    public static string FromKeyText(string keyText)
    {
        var trimmed = keyText.Trim();

        return trimmed.ToLowerInvariant() switch
        {
            "comma" => ",",
            "period" or "dot" => ".",
            "semicolon" => ";",
            "colon" => ":",
            "slash" or "forwardslash" => "/",
            "backslash" => "\\",
            _ => Normalize(trimmed),
        };
    }
}
