using Fleet.Shared.Evidence;

namespace Fleet.Tests.Shared.Evidence;

public sealed class ReportDriftTests
{
    private static ReportEvidence Evidence(string head) => new("/w", "main", "m", head, false);

    [Fact]
    public void One_commit_reads_in_the_singular()
    {
        Assert.Equal("1 commit after report", new ReportDrift(1, false).Label);
        Assert.Equal("2 commits after report", new ReportDrift(2, false).Label);
    }

    [Fact]
    public void Only_a_done_report_with_a_recorded_head_is_checked()
    {
        Assert.Equal("abc", ReportDrift.HeadToCheck("done", Evidence("abc")));
        Assert.Equal("abc", ReportDrift.HeadToCheck(" Done ", Evidence("abc")));
        Assert.Equal(string.Empty, ReportDrift.HeadToCheck("working", Evidence("abc")));
        Assert.Equal(string.Empty, ReportDrift.HeadToCheck("failed", Evidence("abc")));
        Assert.Equal(string.Empty, ReportDrift.HeadToCheck(string.Empty, Evidence("abc")));
        Assert.Equal(string.Empty, ReportDrift.HeadToCheck("done", Evidence(string.Empty)));
        Assert.Equal(string.Empty, ReportDrift.HeadToCheck("done", null));
    }
}
