using Fleet.Ports.Approvals.Models;

namespace Fleet.Ports.Approvals;

public interface IApprovalChannel
{
    Task<ApprovalOutcome> AskAsync(ApprovalRequest request, CancellationToken ct = default);
}
