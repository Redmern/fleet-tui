namespace Fleet.Ports.Approvals.Models;

public sealed record PendingApproval(string Id, ApprovalRequest Request);
