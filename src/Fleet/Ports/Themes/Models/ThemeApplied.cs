using Fleet.Ports.Themes.Enums;

namespace Fleet.Ports.Themes.Models;

public sealed record ThemeApplied(string Tool, ThemeOutcome Outcome, string Detail)
{
    public static ThemeApplied Applied(string tool, string detail) => new(tool, ThemeOutcome.Applied, detail);

    public static ThemeApplied Unchanged(string tool, string detail) => new(tool, ThemeOutcome.Unchanged, detail);

    public static ThemeApplied Skipped(string tool, string detail) => new(tool, ThemeOutcome.Skipped, detail);

    public static ThemeApplied Failed(string tool, string detail) => new(tool, ThemeOutcome.Failed, detail);

    public string Line => $"{Tool,-8} {Outcome.ToString().ToLowerInvariant()}: {Detail}";
}
