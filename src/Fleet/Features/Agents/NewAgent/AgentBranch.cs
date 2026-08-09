using Fleet.Features.Agents.NewAgent.Models;
using Fleet.Shared.Results;

namespace Fleet.Features.Agents.NewAgent;

public static class AgentBranch
{
    public const string NothingGiven =
        "Give a branch name, a base, or both — otherwise there is nothing to work on.";

    public static Result<AgentBranchPlan> Plan(string branchName, string @base)
    {
        var name = branchName.Trim();
        var from = @base.Trim();

        if (name.Length == 0 && from.Length == 0)
        {
            return Result<AgentBranchPlan>.Fail(NothingGiven);
        }

        if (name.Length == 0)
        {
            return Result<AgentBranchPlan>.Ok(new AgentBranchPlan(ShortNameOf(from), from));
        }

        return Result<AgentBranchPlan>.Ok(new AgentBranchPlan(name, from));
    }

    public static string ShortNameOf(string reference) =>
        reference.StartsWith("origin/", StringComparison.Ordinal)
            ? reference["origin/".Length..]
            : reference;

    public static bool IsRemote(string reference) =>
        reference.StartsWith("origin/", StringComparison.Ordinal);
}
