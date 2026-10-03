using Fleet.Features.Agents.ListAgents;
using Fleet.Features.Dashboard.ShowDashboard.Models;
using Fleet.Features.Notifications.ShowNotices;
using Fleet.Features.Orchestrations.ListSubs;
using Fleet.Features.Repositories.ListRepositories;
using Fleet.Ports.Agents.Models;
using Fleet.Ports.Notifications.Models;
using Fleet.Shared;

namespace Fleet.Cli.Composition;

public static class DashboardSkeleton
{
    public static IReadOnlyList<RepositoryChoice> Repositories(string projectRoot) =>
        [.. RepositoryFolders.Skim(projectRoot).Select(r => new RepositoryChoice(r.Name, r.Path, r.DefaultBranch))];

    public static AgentBoard Agents(IReadOnlyList<AgentRecord> stored)
    {
        var board = SubTree.Of(stored).Board;

        return new AgentBoard(
            AgentRows.For(board),
            board.Count,
            [.. board.Select(a => a.Hidden)],
            [.. board.Select(a => a.Status)]);
    }

    public static SubBoard Subs(IReadOnlyList<AgentRecord> stored, string trigger)
    {
        var listing = SubTree.Of(stored);

        return new SubBoard(
            SubRows.For(listing, _ => BranchState.Unknown, trigger),
            listing.Flat.Count(e => !e.IsChild),
            [.. listing.Flat.Select(e => e.Agent.Hidden)],
            SubRows.GapsAfter(listing));
    }

    public static NoticeBoard Notices(IReadOnlyList<Notice> all, DateTime now) =>
        new(NoticeRows.For(all, withProject: false, now),
            [.. all.Select(n => n.Key)],
            [.. all.Select(n => n.Worktree)],
            all.Count(n => n.IsOpen));
}
