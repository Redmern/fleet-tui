using System.Runtime.InteropServices;
using Fleet.Shared.Releases;

namespace Fleet.Tests.Shared;

public class ReleaseAssetNamesTests
{
    [Fact]
    public void Windows_x64_gets_the_exe_asset()
    {
        Assert.Equal(
            "fleet-win-x64.exe",
            ReleaseAssetNames.For(OSPlatform.Windows, System.Runtime.InteropServices.Architecture.X64));
    }

    [Fact]
    public void Linux_x64_gets_the_extensionless_asset()
    {
        Assert.Equal(
            "fleet-linux-x64",
            ReleaseAssetNames.For(OSPlatform.Linux, System.Runtime.InteropServices.Architecture.X64));
    }

    [Fact]
    public void MacOS_has_no_published_asset_yet()
    {
        Assert.Null(
            ReleaseAssetNames.For(OSPlatform.OSX, System.Runtime.InteropServices.Architecture.X64));
    }

    [Fact]
    public void Arm_has_no_published_asset_yet()
    {
        Assert.Null(
            ReleaseAssetNames.For(
                OSPlatform.Windows, System.Runtime.InteropServices.Architecture.Arm64));
    }
}
