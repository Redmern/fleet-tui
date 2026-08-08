using Fleet.Platform.Mux.WezTerm;

namespace Fleet.Tests.Platform.Mux;

public class CwdUrlTests
{
    [Theory]
    [InlineData("file:///C:/repos/fleet", "C:/repos/fleet")]
    [InlineData("file:///home/red/repos", "/home/red/repos")]
    [InlineData("file://hostname/C:/repos", "hostname/C:/repos")]
    [InlineData(@"C:\repos\fleet", "C:/repos/fleet")]
    [InlineData("/home/red", "/home/red")]
    [InlineData("", "")]
    public void Normalize_handles_both_platforms(string input, string expected)
        => Assert.Equal(expected, CwdUrl.Normalize(input));
}
