namespace Fleet.Shared.Settings;

public static class GitGates
{
    public const string CommitRule = "Bash(git commit:*)";

    public const string PushRule = "Bash(git push:*)";

    public static bool IsOwned(string rule) =>
        rule.Equals(CommitRule, StringComparison.Ordinal)
        || rule.Equals(PushRule, StringComparison.Ordinal);
}
