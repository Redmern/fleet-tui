using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;

namespace Fleet.Tests.Ui;

public class FleetMnemonicsTests
{
    [Fact]
    public void Each_label_gets_its_first_free_letter()
    {
        Assert.Equal(["r", "b", "a", "o"], FleetMnemonics.For(["Repo", "Branch", "Base", "Opens"], Keymap.Default));
    }

    [Fact]
    public void No_two_labels_share_a_letter()
    {
        var keys = FleetMnemonics.For(["Branch", "Base", "Bar", "Baz"], Keymap.Default);

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void A_letter_bound_to_a_list_motion_is_never_used()
    {
        var keys = FleetMnemonics.For(["Jump", "Kite"], Keymap.Default);

        Assert.DoesNotContain("j", keys);
        Assert.DoesNotContain("k", keys);
    }

    [Fact]
    public void A_motion_rebound_by_the_user_frees_the_old_letter_and_takes_the_new_one()
    {
        var keymap = new Keymap(KeymapConfig.Default.With(FleetAction.MoveDown, "r"));

        Assert.NotEqual("r", FleetMnemonics.For(["Repo"], keymap)[0]);
    }

    [Fact]
    public void Letters_another_part_of_the_dialog_uses_are_skipped()
    {
        Assert.Equal(["e"], FleetMnemonics.For(["Create"], Keymap.Default, taken: ["c", "r"]));
    }

    [Fact]
    public void A_label_with_no_free_letter_falls_back_to_any_free_one()
    {
        var keys = FleetMnemonics.For(["jk", "jk"], Keymap.Default);

        Assert.All(keys, k => Assert.Single(k));
        Assert.NotEqual(keys[0], keys[1]);
    }
}
