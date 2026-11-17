using Fleet.Shared.Evidence;

namespace Fleet.Tests.Shared.Evidence;

public sealed class ReportFlagsTests
{
    private static ReportEvidence Evidence(int? exit = null, bool dirty = false) =>
        new("/w", "origin/main", "aaaaaaa1111", "bbbbbbb2222222", dirty, exit is null ? string.Empty : "dotnet test", exit);

    [Fact]
    public void A_done_report_without_verify_fields_is_unverified()
    {
        Assert.Equal("done (unverified)", ReportFlags.Label("done", false, Evidence()));
    }

    [Fact]
    public void A_done_report_with_no_evidence_at_all_is_unverified_too()
    {
        Assert.Equal("done (unverified)", ReportFlags.Label("done", false, null));
    }

    [Fact]
    public void A_done_report_with_a_non_zero_exit_code_is_verify_failed()
    {
        Assert.Equal("done (verify failed)", ReportFlags.Label("done", false, Evidence(exit: 1)));
        Assert.Equal("done (verify failed)", ReportFlags.Label("done", false, Evidence(exit: -1)));
    }

    [Fact]
    public void A_done_report_in_a_dirty_worktree_is_flagged_dirty()
    {
        Assert.Equal("done (dirty)", ReportFlags.Label("done", false, Evidence(exit: 0, dirty: true)));
    }

    [Fact]
    public void A_clean_done_report_with_a_passing_verify_has_no_flag()
    {
        Assert.Equal("done", ReportFlags.Label("done", false, Evidence(exit: 0)));
        Assert.Empty(ReportFlags.Of("done", false, Evidence(exit: 0)));
    }

    [Fact]
    public void Every_flag_that_applies_is_shown_in_one_fixed_order()
    {
        Assert.Equal("done (unverified) (dirty)", ReportFlags.Label("done", false, Evidence(dirty: true)));
        Assert.Equal("done (verify failed) (dirty)", ReportFlags.Label("done", false, Evidence(exit: 2, dirty: true)));
        Assert.Equal(["verify failed", "dirty"], ReportFlags.Of("done", false, Evidence(exit: 2, dirty: true)));
    }

    [Fact]
    public void A_sub_orchestrator_is_exempt_from_the_unverified_flag_but_not_from_the_others()
    {
        Assert.Equal("done", ReportFlags.Label("done", true, Evidence()));
        Assert.Equal("done", ReportFlags.Label("done", true, null));
        Assert.Equal("done (verify failed)", ReportFlags.Label("done", true, Evidence(exit: 1)));
    }

    [Fact]
    public void Only_a_done_report_is_flagged()
    {
        Assert.Equal("failed", ReportFlags.Label("failed", false, Evidence(exit: 1, dirty: true)));
        Assert.Equal("working", ReportFlags.Label("working", false, Evidence(dirty: true)));
        Assert.Equal(string.Empty, ReportFlags.Label(string.Empty, false, null));
    }

    [Fact]
    public void The_verify_word_is_ok_failed_or_unverified()
    {
        Assert.Equal("ok", ReportFlags.Verify(Evidence(exit: 0)));
        Assert.Equal("failed", ReportFlags.Verify(Evidence(exit: 3)));
        Assert.Equal("unverified", ReportFlags.Verify(Evidence()));
        Assert.Equal("unverified", ReportFlags.Verify(null));
    }

    [Fact]
    public void The_short_head_is_seven_characters_and_a_short_one_is_left_alone()
    {
        Assert.Equal("bbbbbbb", Evidence().ShortHead);
        Assert.Equal("abc", (Evidence() with { Head = "abc" }).ShortHead);
        Assert.Equal(string.Empty, (Evidence() with { Head = string.Empty }).ShortHead);
    }
}
