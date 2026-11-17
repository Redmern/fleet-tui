using Fleet.Features.Orchestrations.Flags;
using Fleet.Features.Orchestrations.Flags.Models;

namespace Fleet.Tests.Features.Orchestrations.Flags;

// AC-19: which pairs of live agents of one owner changed the same files, and when that is worth a (new) notice.
// Rule: a pair is noticed when its shared set holds a file no earlier notice for that pair named. The same set
// again, a smaller set, or a set that grows back to files already named sends nothing.
public sealed class OverlapsTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> NoneSent =
        new Dictionary<string, IReadOnlySet<string>>();

    private static ChangedFiles Changed(string branch, params string[] files) => new("api", branch, files);

    private static Dictionary<string, IReadOnlySet<string>> Sent(string key, params string[] files) =>
        new(StringComparer.OrdinalIgnoreCase) { [key] = new HashSet<string>(files, StringComparer.Ordinal) };

    [Fact]
    public void Two_agents_that_changed_a_file_in_common_are_a_pair_with_the_shared_files_sorted()
    {
        var due = Overlaps.Due(
            [Changed("feat/b", "src/z.cs", "src/a.cs", "only-b.cs"), Changed("feat/a", "src/a.cs", "src/z.cs", "only-a.cs")],
            NoneSent);

        var pair = Assert.Single(due);
        Assert.Equal("api/feat/a", pair.First);
        Assert.Equal("api/feat/b", pair.Second);
        Assert.Equal(["src/a.cs", "src/z.cs"], pair.Files);
        Assert.Equal("api/feat/a|api/feat/b", pair.Key);
    }

    [Fact]
    public void Agents_without_a_file_in_common_or_in_other_repositories_are_no_pair()
    {
        Assert.Empty(Overlaps.Due([Changed("feat/a", "a.cs"), Changed("feat/b", "b.cs")], NoneSent));
        Assert.Empty(Overlaps.Due([Changed("feat/a", "README.md"), new ChangedFiles("web", "feat/b", ["README.md"])], NoneSent));
        Assert.Empty(Overlaps.Due([Changed("feat/a", "a.cs")], NoneSent));
    }

    [Fact]
    public void Three_agents_give_every_overlapping_pair()
    {
        var due = Overlaps.Due(
            [Changed("feat/a", "x.cs", "y.cs"), Changed("feat/b", "x.cs"), Changed("feat/c", "y.cs")], NoneSent);

        Assert.Equal(["api/feat/a|api/feat/b", "api/feat/a|api/feat/c"], due.Select(p => p.Key));
    }

    [Fact]
    public void The_same_shared_set_again_is_not_sent_again()
    {
        var due = Overlaps.Due(
            [Changed("feat/a", "x.cs", "y.cs"), Changed("feat/b", "x.cs", "y.cs")],
            Sent("api/feat/a|api/feat/b", "x.cs", "y.cs"));

        Assert.Empty(due);
    }

    [Fact]
    public void A_grown_shared_set_is_sent_again_with_every_shared_file()
    {
        var due = Overlaps.Due(
            [Changed("feat/a", "x.cs", "y.cs"), Changed("feat/b", "x.cs", "y.cs")],
            Sent("api/feat/a|api/feat/b", "x.cs"));

        Assert.Equal(["x.cs", "y.cs"], Assert.Single(due).Files);
    }

    [Fact]
    public void A_shrunk_set_or_one_grown_back_to_files_already_named_is_not_sent()
    {
        var sent = Sent("api/feat/a|api/feat/b", "x.cs", "y.cs");

        Assert.Empty(Overlaps.Due([Changed("feat/a", "x.cs"), Changed("feat/b", "x.cs")], sent));
        Assert.Empty(Overlaps.Due([Changed("feat/a", "x.cs", "y.cs"), Changed("feat/b", "x.cs", "y.cs")], sent));
    }

    [Fact]
    public void A_set_that_swaps_in_a_file_never_named_is_sent()
    {
        var due = Overlaps.Due(
            [Changed("feat/a", "x.cs", "z.cs"), Changed("feat/b", "x.cs", "z.cs")],
            Sent("api/feat/a|api/feat/b", "x.cs", "y.cs"));

        Assert.Equal(["x.cs", "z.cs"], Assert.Single(due).Files);
    }

    [Fact]
    public void The_pair_key_does_not_depend_on_the_order_or_the_case_of_the_names()
    {
        var due = Overlaps.Due(
            [Changed("Feat/B", "x.cs"), Changed("feat/a", "x.cs")],
            Sent("API/FEAT/A|api/feat/b", "x.cs"));

        Assert.Empty(due);
    }

    [Fact]
    public void The_notice_names_both_agents_and_the_files()
    {
        var pair = new OverlapPair("api/feat/a", "api/feat/b", ["src/a.cs", "src/z.cs"]);

        Assert.Equal(
            "api/feat/a and api/feat/b changed the same 2 file(s) against their bases: src/a.cs, src/z.cs. "
            + "Nothing was blocked; check that their work does not collide",
            Overlaps.Line(pair));
    }

    [Fact]
    public void A_long_file_list_is_cut_with_a_count_of_the_rest()
    {
        var files = Enumerable.Range(0, 25).Select(i => $"f{i:00}.cs").ToList();

        var line = Overlaps.Line(new OverlapPair("api/feat/a", "api/feat/b", files));

        Assert.Contains("the same 25 file(s)", line);
        Assert.Contains("f19.cs and 5 more.", line);
        Assert.DoesNotContain("f20.cs", line);
    }

    [Fact]
    public void The_ledger_round_trips_what_was_sent_and_adds_up_appended_notices()
    {
        var text = OverlapLedger.Lines("api/feat/a|api/feat/b", ["x.cs"])
                   + OverlapLedger.Lines("api/feat/a|api/feat/b", ["x.cs", "y.cs"])
                   + OverlapLedger.Lines("api/feat/a|api/feat/c", ["z.cs"])
                   + "a line without a tab\n";

        var sent = OverlapLedger.Parse(text);

        Assert.Equal(["x.cs", "y.cs"], sent["API/feat/a|api/feat/b"].Order(StringComparer.Ordinal));
        Assert.Equal(["z.cs"], sent["api/feat/a|api/feat/c"]);
        Assert.Equal(2, sent.Count);
    }
}
