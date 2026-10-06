using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Orchestrations.Enums;

namespace Fleet.Shared.Orchestrations.Models;

public sealed record DispatchRoute(
    DispatchTarget Target,
    string Prompt,
    (Profile Profile, ProfileSource Source)? Aidlc,
    bool Research);
