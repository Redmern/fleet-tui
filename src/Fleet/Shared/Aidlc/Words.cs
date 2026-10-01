namespace Fleet.Shared.Aidlc;

public static class Words
{
    public static string Of<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

    public static T? Parse<T>(string? text)
        where T : struct, Enum
    {
        var trimmed = text?.Trim() ?? string.Empty;

        if (trimmed.Length == 0 || !trimmed.All(char.IsLetter))
        {
            return null;
        }

        return Enum.TryParse<T>(trimmed, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }
}
