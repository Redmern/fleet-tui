using Fleet.Shared.Iso;

namespace Fleet.Tests.Shared.Iso;

public sealed class IsoProjectionTests
{
    [Theory]
    [InlineData("working", true, IsoProjection.Working)]
    [InlineData("waiting", true, IsoProjection.Waiting)]
    [InlineData("idle", true, IsoProjection.Idle)]
    [InlineData("done", false, IsoProjection.Done)]
    [InlineData("failed", true, IsoProjection.Failed)]
    [InlineData("", false, IsoProjection.Stopped)]
    [InlineData("", true, IsoProjection.Idle)]
    public void Known_statuses_map_to_their_state(string status, bool open, string state) =>
        Assert.Equal(state, IsoProjection.State(status, open));

    [Fact]
    public void Free_text_never_passes_through()
    {
        var state = IsoProjection.State("fixed the ACME invoice export for customer 4411", open: true);

        Assert.Equal(IsoProjection.Working, state);
    }

    [Theory]
    [InlineData("Done", IsoProjection.Done)]
    [InlineData("NeedsInput", IsoProjection.Waiting)]
    [InlineData("Permission", IsoProjection.Waiting)]
    [InlineData("BranchTrouble", IsoProjection.BranchTrouble)]
    public void Notice_kinds_map_to_fixed_words(string kind, string text) =>
        Assert.Equal(text, IsoProjection.Notice(kind));

    [Fact]
    public void A_line_is_code_colon_state() =>
        Assert.Equal("sub1.agent2: done", IsoProjection.Line("sub1.agent2", IsoProjection.Done));
}
