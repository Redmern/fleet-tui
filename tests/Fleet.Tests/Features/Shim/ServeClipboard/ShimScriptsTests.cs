using Fleet.Features.Shim.ServeClipboard;

namespace Fleet.Tests.Features.Shim.ServeClipboard;

public class ShimScriptsTests
{
    [Fact]
    public void A_shim_script_hands_its_name_and_arguments_to_fleet_shim()
    {
        var script = ShimScripts.Script("/opt/my fleet/it's/fleet", "xclip");

        Assert.StartsWith("#!/bin/sh\n", script);
        Assert.Contains("exec '/opt/my fleet/it'\\''s/fleet' shim xclip \"$@\"\n", script);
    }

    [Theory]
    [InlineData("/usr/bin:/bin", "/shims:/usr/bin:/bin")]
    [InlineData("/shims:/usr/bin", "/shims:/usr/bin")]
    [InlineData("/usr/bin:/shims", "/shims:/usr/bin")]
    [InlineData("", "/shims")]
    [InlineData(null, "/shims")]
    public void The_shim_folder_goes_first_on_path_once(string? inherited, string expected) =>
        Assert.Equal(expected, ShimScripts.PathWith("/shims", inherited));

    [Fact]
    public void The_real_tool_is_the_first_on_path_outside_the_shim_folder()
    {
        var found = ShimScripts.RealTool("xclip", "/shims:/usr/local/bin:/usr/bin", "/shims", p => p is "/shims/xclip" or "/usr/bin/xclip");

        Assert.Equal("/usr/bin/xclip", found);
        Assert.Null(ShimScripts.RealTool("wl-paste", "/shims:/usr/bin", "/shims", p => p == "/shims/wl-paste"));
    }
}
