using Fleet.Features.Agents.RemoveAgent;

namespace Fleet.Tests.Features.Agents;

public class WorktreeDirtTests
{
    [Fact]
    public void A_clean_worktree_has_nothing_to_lose()
    {
        Assert.Empty(WorktreeDirt.Parse(string.Empty));
    }

    [Fact]
    public void Modified_and_untracked_files_both_count()
    {
        var changed = WorktreeDirt.Parse(" M src/Program.cs\n?? notes.txt\n");

        Assert.Equal(["src/Program.cs", "notes.txt"], changed);
    }

    [Fact]
    public void Fleets_own_directory_is_ignored_so_every_agent_is_not_permanently_dirty()
    {
        var changed = WorktreeDirt.Parse("?? .fleet/ready\n?? .fleet/launch.ps1\n");

        Assert.Empty(changed);
    }

    [Fact]
    public void A_backslash_path_is_still_recognised_as_fleets_own()
    {
        Assert.Empty(WorktreeDirt.Parse(@"?? .fleet\ready" + "\n"));
    }

    [Fact]
    public void Real_work_alongside_fleets_files_is_still_reported()
    {
        var changed = WorktreeDirt.Parse("?? .fleet/ready\n M src/Program.cs\n");

        Assert.Equal(["src/Program.cs"], changed);
    }

    [Fact]
    public void A_file_merely_named_like_fleets_directory_is_not_ignored()
    {
        var changed = WorktreeDirt.Parse("?? .fleetrc\n");

        Assert.Equal([".fleetrc"], changed);
    }
}
