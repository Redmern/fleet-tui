using Fleet.Ports.Forwards.Enums;

namespace Fleet.Ports.Forwards.Models;

public sealed record ViewerForwardResult(ViewerForwardState State, string Url, string? Reason = null);
