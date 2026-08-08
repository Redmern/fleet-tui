using System.Text;
using Fleet.Platform.Mux.WezTerm;

namespace Fleet.Tests.Platform.Mux;

public class WezTermUserVarsTests
{
    [Fact]
    public void The_sequence_is_an_osc_1337_set_user_var()
    {
        var sequence = WezTermUserVars.Sequence("fleet", "dashboard");

        Assert.StartsWith("]1337;SetUserVar=fleet=", sequence);
        Assert.EndsWith("", sequence);
    }

    [Fact]
    public void The_value_is_base64_encoded_as_wezterm_requires()
    {
        var sequence = WezTermUserVars.Sequence("fleet", "dashboard");

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("dashboard"));

        Assert.Contains(expected, sequence, StringComparison.Ordinal);
        Assert.DoesNotContain("=dashboard", sequence, StringComparison.Ordinal);
    }

    [Fact]
    public void The_dashboard_marker_uses_the_name_the_generated_lua_looks_for()
    {
        var lua = WezTermKeybinds.Generate(global::Fleet.Ui.Keymap.Default, "fleet");

        Assert.Contains($"vars['{WezTermUserVars.FleetVar}']", lua, StringComparison.Ordinal);
    }
}
