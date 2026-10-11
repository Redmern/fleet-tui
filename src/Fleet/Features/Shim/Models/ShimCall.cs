using Fleet.Features.Shim.Enums;

namespace Fleet.Features.Shim.Models;

public sealed record ShimCall(string Tool, ShimRead Read, string? Mime = null);
