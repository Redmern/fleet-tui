namespace Fleet.Ports.Approvals.Models;

public sealed record ApprovalRequest(string Project, string Tool, string Summary);
