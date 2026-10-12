using Fleet.Features.Shim.Mac.Enums;

namespace Fleet.Features.Shim.Mac.Models;

public sealed record MacShimCall(string Tool, MacRead Read, string? File = null);
