using Fleet.Shared.Keymap.Enums;
using Fleet.Shared.Keymap.Models;
using Fleet.Ui;

namespace Fleet.Tests.Ui;

public class KeymapSignatureTests
{
    [Fact]
    public void The_same_bindings_produce_the_same_signature_whatever_the_order()
    {
        var one = new Keymap(new KeymapConfig(
            "Ctrl+Enter",
            new Dictionary<FleetAction, string>
            {
                [FleetAction.Refresh] = "r",
                [FleetAction.Close] = "q",
            }));

        var two = new Keymap(new KeymapConfig(
            "Ctrl+Enter",
            new Dictionary<FleetAction, string>
            {
                [FleetAction.Close] = "q",
                [FleetAction.Refresh] = "r",
            }));

        Assert.Equal(one.Signature, two.Signature);
    }

    [Fact]
    public void A_rebound_key_changes_the_signature_so_the_dashboard_notices()
    {
        var before = Keymap.Default;

        var after = new Keymap(KeymapConfig.Default.With(FleetAction.Refresh, "F5"));

        Assert.NotEqual(before.Signature, after.Signature);
    }

    [Fact]
    public void A_new_prefix_changes_the_signature_too()
    {
        Assert.NotEqual(
            Keymap.Default.Signature,
            new Keymap(KeymapConfig.Default.WithPrefix("Ctrl+A")).Signature);
    }
}
