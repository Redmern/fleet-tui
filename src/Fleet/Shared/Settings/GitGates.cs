namespace Fleet.Shared.Settings;

public static class GitGates
{
    public const string CommitRule = "Bash(git commit:*)";

    public const string PushRule = "Bash(git push:*)";

    public const string MergeRule = "Bash(gh pr merge:*)";

    public const string MergePowerShellRule = "PowerShell(gh pr merge:*)";

    public static bool IsOwned(string rule) =>
        rule.Equals(CommitRule, StringComparison.Ordinal)
        || rule.Equals(PushRule, StringComparison.Ordinal)
        || rule.Equals(MergeRule, StringComparison.Ordinal)
        || rule.Equals(MergePowerShellRule, StringComparison.Ordinal);
}
