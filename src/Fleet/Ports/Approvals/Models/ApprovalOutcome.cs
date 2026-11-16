using Fleet.Ports.Approvals.Enums;

namespace Fleet.Ports.Approvals.Models;

public sealed record ApprovalOutcome(ApprovalDecision Decision, string Reason)
{
    public bool Allowed => Decision == ApprovalDecision.Allowed;

    public static ApprovalOutcome Allow => new(ApprovalDecision.Allowed, string.Empty);

    public static ApprovalOutcome Deny(string reason) => new(ApprovalDecision.Denied, reason);

    public static ApprovalOutcome Expired(string reason) => new(ApprovalDecision.Expired, reason);

    public static ApprovalOutcome NoAnswer(ApprovalRequest request, TimeSpan waited) =>
        Expired(
            $"No one answered the approval prompt (shown over {request.CallerName} in {request.Project}) within {(int)waited.TotalSeconds} s");

    public static ApprovalOutcome NeverShown(ApprovalRequest request, TimeSpan allowed) =>
        Expired(
            $"The approval prompt for {request.CallerName} in {request.Project} was never shown: the {(int)allowed.TotalSeconds} s it was allowed ran out while it waited behind other approvals");

    public static ApprovalOutcome TooLate(ApprovalRequest request, TimeSpan allowed) =>
        Expired(
            $"The approval for {request.CallerName} in {request.Project} came in time, but its turn to run came after the {(int)allowed.TotalSeconds} s its client allows, so the action was not run");

    public static ApprovalOutcome NoDashboard(string reason) =>
        new(ApprovalDecision.NoDashboard, reason);
}
