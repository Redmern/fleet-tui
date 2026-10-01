using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Models;

namespace Fleet.Tests.Shared.Aidlc;

public class TraceabilityTests
{
    private static WorkUnit Claiming(string id, params string[] acceptance) =>
        new(id, id, "repo", id, [], acceptance, [], "true");

    [Fact]
    public void Every_criterion_covered_by_at_least_one_unit_is_complete()
    {
        var report = Traceability.Check(
            ["AC-1", "AC-2", "AC-3"],
            [Claiming("U1", "AC-1", "AC-3"), Claiming("U2", "AC-2", "AC-3")]);

        Assert.True(report.Complete);
    }

    [Fact]
    public void A_criterion_no_unit_claims_is_uncovered()
    {
        var report = Traceability.Check(["AC-1", "AC-2"], [Claiming("U1", "AC-1")]);

        Assert.False(report.Complete);
        Assert.Equal(["AC-2"], report.Uncovered);
    }

    [Fact]
    public void A_unit_claiming_a_criterion_the_spec_lacks_is_reported()
    {
        var report = Traceability.Check(["AC-1"], [Claiming("U1", "AC-1", "AC-9")]);

        Assert.False(report.Complete);
        Assert.Equal(["AC-9"], report.Unknown);
    }

    [Fact]
    public void Ids_compare_without_case_or_surrounding_space()
    {
        Assert.True(Traceability.Check([" ac-1 "], [Claiming("U1", "AC-1")]).Complete);
    }

    [Fact]
    public void No_units_leave_every_criterion_uncovered()
    {
        Assert.Equal(["AC-1", "AC-2"], Traceability.Check(["AC-1", "AC-2"], []).Uncovered);
    }
}
