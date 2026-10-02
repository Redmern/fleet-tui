using Fleet.Features.Menu.EditAidlc;
using Fleet.Shared.Aidlc;
using Fleet.Shared.Aidlc.Enums;
using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;
using Fleet.Ui.Constants;

namespace Fleet.Tests.Features.Menu;

public class AidlcRowsTests
{
    [Fact]
    public void Mode_profile_and_autonomy_come_first_then_one_row_per_part()
    {
        var rows = AidlcRows.For(AidlcSettings.Default);

        Assert.Equal(AidlcRows.Count, rows.Count);
        Assert.Contains(AidlcRows.ModeLabel, rows[0].Text);
        Assert.Contains(AidlcRows.ProfileLabel, rows[1].Text);
        Assert.Contains(AidlcRows.AutonomyLabel, rows[2].Text);
        Assert.True(AidlcRows.IsModeRow(0));
        Assert.True(AidlcRows.IsProfileRow(1));
        Assert.True(AidlcRows.IsAutonomyRow(2));
        Assert.Equal(AidlcPart.None, AidlcRows.PartAt(2));
        Assert.Equal(AidlcSettings.Parts, Enumerable.Range(3, AidlcSettings.Parts.Count).Select(AidlcRows.PartAt));
        Assert.Equal(AidlcPart.None, AidlcRows.PartAt(AidlcRows.Count));
    }

    [Fact]
    public void The_values_show_what_is_set()
    {
        var settings = new AidlcSettings(AidlcMode.Manual, Profile.Feature, Autonomy.Automatic, AidlcPart.None);

        var rows = AidlcRows.For(settings);

        Assert.Contains(rows[0].Trailing!, s => s.Text.Trim() == "manual");
        Assert.Contains(rows[1].Trailing!, s => s.Text.Trim() == "feature");
        Assert.Contains(rows[2].Trailing!, s => s.Text.Trim() == "automatic");
    }

    [Fact]
    public void A_part_that_is_off_says_what_off_means()
    {
        var settings = AidlcSettings.Default.With(AidlcPart.Review, on: false).With(AidlcPart.SpecGate, on: false);

        var rows = AidlcRows.For(settings);
        var review = rows[3 + AidlcSettings.Parts.ToList().IndexOf(AidlcPart.Review)];
        var spec = rows[3 + AidlcSettings.Parts.ToList().IndexOf(AidlcPart.SpecGate)];
        var verify = rows[3 + AidlcSettings.Parts.ToList().IndexOf(AidlcPart.Verify)];

        Assert.Contains(review.Trailing!, s => s.Tone == FleetTones.Bad);
        Assert.Contains(review.Trailing!, s => s.Text == "off: the stage is skipped");
        Assert.Contains(spec.Trailing!, s => s.Text == "off: the stage runs, its gate is automatic");
        Assert.Contains(verify.Trailing!, s => s.Tone == FleetTones.Good);
    }

    [Fact]
    public void The_pickers_list_every_choice_in_enum_order()
    {
        Assert.Equal(Enum.GetValues<AidlcMode>().Select(Words.Of), AidlcRows.ModeEntries().Select(e => e.Label));
        Assert.Equal(Enum.GetValues<Profile>().Select(Words.Of), AidlcRows.ProfileEntries().Select(e => e.Label));
        Assert.Equal(Enum.GetValues<Autonomy>().Select(Words.Of), AidlcRows.AutonomyEntries().Select(e => e.Label));
    }

    [Fact]
    public void The_profile_picker_keys_are_distinct()
    {
        var keys = AidlcRows.ProfileEntries().Select(e => e.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }
}
