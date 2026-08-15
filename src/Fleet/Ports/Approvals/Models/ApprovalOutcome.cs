using Fleet.Ports.Approvals.Enums;

namespace Fleet.Ports.Approvals.Models;

public sealed record ApprovalOutcome(ApprovalDecision Decision, string Reason)
{
    public bool Allowed => Decision == ApprovalDecision.Allowed;

    public static ApprovalOutcome Allow => new(ApprovalDecision.Allowed, string.Empty);

    public static ApprovalOutcome Deny(string reason) => new(ApprovalDecision.Denied, reason);

    public static ApprovalOutcome Expired(string reason) => new(ApprovalDecision.Expired, reason);

    public static ApprovalOutcome NoDashboard(string reason) =>
        new(ApprovalDecision.NoDashboard, reason);
}
