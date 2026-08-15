using Fleet.Ports.Approvals;
using Fleet.Ports.Approvals.Models;

namespace Fleet.Platform.Approvals;

public sealed class NullApprovalChannel : IApprovalChannel
{
    public Task<ApprovalOutcome> AskAsync(ApprovalRequest request, CancellationToken ct = default) =>
        Task.FromResult(ApprovalOutcome.NoDashboard(
            "This action needs approval, but fleet cannot reach a dashboard yet."));
}
