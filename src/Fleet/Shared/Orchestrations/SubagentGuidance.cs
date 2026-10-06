namespace Fleet.Shared.Orchestrations;

public static class SubagentGuidance
{
    public const string FileName = "subagent-guidance.md";

    public const string RelativePath = ".fleet/" + FileName;

    public const string AppendFlag = "--append-system-prompt-file";

    public const string ForSubOrchestrators =
        """
        Do reading and research yourself with a subagent (Explore, or a general-purpose
        subagent told not to edit): checking a repository's state, reading a diff, looking
        something up. Do not start a fleet agent for it. A fleet agent is for a change
        that needs its own branch. Fleet already tells every repo agent to use subagents
        for searches, logs, test runs and a review before its PR; you don't need to repeat it.
        """;

    public const string ForRepoAgents =
        """
        # Subagents (from fleet)
        - Delegate searches, logs and test runs whose output you won't need verbatim to a
          subagent: Explore for code search, general-purpose for the rest. Keep only its
          conclusion in your own context.
        - Before you open a PR, run a review subagent (general-purpose, told to read the diff
          and not edit anything) on your branch's diff and deal with what it finds.
        - For throwaway parallel attempts, use subagents with `isolation: worktree` instead of
          asking for more fleet agents. A fleet agent is for work that needs its own branch.
        """;

    public static bool IsIn(string worktree) =>
        File.Exists(Path.Combine(worktree, ".fleet", FileName));

    public static void Apply(string worktree, bool on)
    {
        var folder = Path.Combine(worktree, ".fleet");
        var file = Path.Combine(folder, FileName);

        try
        {
            if (on)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(file, ForRepoAgents);
            }
            else
            {
                File.Delete(file);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
