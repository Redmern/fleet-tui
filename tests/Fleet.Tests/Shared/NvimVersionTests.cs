using Fleet.Shared;

namespace Fleet.Tests.Shared;

public class NvimVersionTests
{
    [Theory]
    [InlineData("NVIM v0.10.2\nBuild type: Release\nLuaJIT 2.1", 0, 10, 2)]
    [InlineData("NVIM v0.12.0-dev-1234+gabc\n", 0, 12, 0)]
    public void The_first_line_of_nvim_version_parses(string output, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), NvimVersion.Parse(output));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("VIM - Vi IMproved 9.1")]
    public void Anything_else_is_no_version(string? output)
    {
        Assert.Null(NvimVersion.Parse(output));
    }

    [Theory]
    [InlineData(0, 8, 3, false)]
    [InlineData(0, 9, 0, true)]
    [InlineData(0, 11, 1, true)]
    public void App_names_need_nvim_0_9(int major, int minor, int build, bool supported)
    {
        Assert.Equal(supported, NvimVersion.SupportsAppName(new Version(major, minor, build)));
    }
}
