namespace Fleet.Features.Agents.RemoveAgent.Models;

public sealed record RemoveSubCommand(
    string Project, string Slug, string Caller, bool DeleteFolder, bool RemoveAgents);

public sealed record SubRemoval(
    string Slug,
    string Folder,
    bool FolderDeleted,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Kept,
    IReadOnlyList<string> Released)
{
    public string Note
    {
        get
        {
            var parts = new List<string>
            {
                FolderDeleted
                    ? $"removed {Slug} and deleted its folder."
                    : $"removed {Slug}; its folder stays at {Folder}.",
            };

            if (Removed.Count > 0)
            {
                parts.Add($"Removed {Removed.Count} of its agent(s) with their worktrees: {string.Join(", ", Removed)}.");
            }

            if (Kept.Count > 0)
            {
                parts.Add($"Kept {Kept.Count} agent(s) as top-level agents instead of deleting them: {string.Join("; ", Kept)}.");
            }

            if (Released.Count > 0)
            {
                parts.Add(
                    $"{Released.Count} agent(s) stay registered and keep running as top-level agents: "
                    + $"{string.Join(", ", Released)}.");
            }

            return string.Join(' ', parts);
        }
    }
}
