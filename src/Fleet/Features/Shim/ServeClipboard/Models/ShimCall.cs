using Fleet.Features.Shim.ServeClipboard.Enums;

namespace Fleet.Features.Shim.ServeClipboard.Models;

public sealed record ShimCall(string Tool, ShimRead Read, string? Mime = null);
