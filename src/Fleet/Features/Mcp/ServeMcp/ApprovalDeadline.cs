using Fleet.Ports.Approvals.Models;

namespace Fleet.Features.Mcp.ServeMcp;

public static class ApprovalDeadline
{
    public static readonly TimeSpan Default = ApprovalRequest.DefaultWait;

    private static readonly TimeSpan Margin = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Floor = TimeSpan.FromSeconds(1);

    public static TimeSpan For(TimeSpan? clientLimit)
    {
        if (clientLimit is not { } limit)
        {
            return Default;
        }

        var wait = limit - Margin;

        return wait < Floor ? Floor : wait < Default ? wait : Default;
    }
}
