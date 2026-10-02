namespace Fleet.Shared.Aidlc.Enums;

[Flags]
public enum AidlcPart
{
    None = 0,
    SpecGate = 1,
    PlanGate = 2,
    DeliverGate = 4,
    Verify = 8,
    Review = 16,
    Learn = 32,
    WalkingSkeleton = 64,
}
