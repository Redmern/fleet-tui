namespace Fleet.Shared.Settings;

public static class ModelChoice
{
    public const string Inherit = "inherit";

    public const string ModelFlag = "--model";

    public const string EffortFlag = "--effort";

    public static IReadOnlyList<string> Efforts { get; } = ["low", "medium", "high", "xhigh", "max"];

    public static bool IsInherit(string value) => Model(value) == Inherit;

    public static string Model(string value)
    {
        var trimmed = value.Trim();

        return trimmed.Length > 0
            && !trimmed.Equals(Inherit, StringComparison.OrdinalIgnoreCase)
            && trimmed.All(IsModelChar)
            && char.IsAsciiLetterOrDigit(trimmed[0])
                ? trimmed
                : Inherit;
    }

    public static string Effort(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();

        return Efforts.Contains(trimmed) ? trimmed : Inherit;
    }

    private static bool IsModelChar(char c) =>
        char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '/' or '@' or '[' or ']';
}
