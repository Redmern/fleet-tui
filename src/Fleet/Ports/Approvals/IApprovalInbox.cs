using Fleet.Ports.Approvals.Enums;
using Fleet.Ports.Approvals.Models;

namespace Fleet.Ports.Approvals;

public interface IApprovalInbox
{
    void Heartbeat(string project);

    void Retire(string project);

    PendingApproval? TakePending(string project);

    void Answer(string project, string id, ApprovalDecision decision);
}
