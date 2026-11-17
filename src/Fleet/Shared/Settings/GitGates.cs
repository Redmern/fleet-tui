namespace Fleet.Shared.Settings;

public static class GitGates
{
    public const string CommitRule = "Bash(git commit:*)";

    public const string PushRule = "Bash(git push:*)";

    public const string MergeRule = "Bash(gh pr merge:*)";

    public const string MergePowerShellRule = "PowerShell(gh pr merge:*)";

    public const string PrCreateRule = "Bash(gh pr create:*)";

    public const string PrGuidance =
        "Open a pull request with fleet's open_pr tool: fleet asks the user, pushes the branch and opens the "
        + "pull request on the repository's forge. Do not push for a pull request or run a forge command "
        + "line yourself.";

    public static bool IsOwned(string rule) =>
        rule.Equals(CommitRule, StringComparison.Ordinal)
        || rule.Equals(PushRule, StringComparison.Ordinal)
        || rule.Equals(MergeRule, StringComparison.Ordinal)
        || rule.Equals(MergePowerShellRule, StringComparison.Ordinal)
        || rule.Equals(PrCreateRule, StringComparison.Ordinal);
}
