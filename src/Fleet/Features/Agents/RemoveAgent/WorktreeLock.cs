namespace Fleet.Features.Agents.RemoveAgent;

public static class WorktreeLock
{
    public static bool LooksBusy(string message) =>
        message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)
        || message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
        || message.Contains("failed to delete", StringComparison.OrdinalIgnoreCase);

    public static bool AlreadyUnregistered(string message) =>
        message.Contains("is not a working tree", StringComparison.OrdinalIgnoreCase);

    public static string Busy(string worktree) =>
        $"{worktree} is still in use, so its files were kept. Something the agent started "
      + "is still running - close it and remove the agent again.";

    public static string Kept(string folder) =>
        $"{folder} could not be deleted because something still uses it, so the folder was kept. "
      + "The agent itself is removed; close whatever holds the folder and delete it manually.";
}
