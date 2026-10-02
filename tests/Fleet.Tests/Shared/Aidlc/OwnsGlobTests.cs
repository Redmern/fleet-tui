using Fleet.Shared.Aidlc;

namespace Fleet.Tests.Shared.Aidlc;

public class OwnsGlobTests
{
    [Theory]
    [InlineData("src/a.cs", "src/a.cs")]
    [InlineData("src/A.cs", "src\\a.cs")]
    [InlineData("src/**", "src/deep/down/a.cs")]
    [InlineData("**", "anything.txt")]
    [InlineData("src/*.cs", "src/a.cs")]
    [InlineData("src/a?.cs", "src/ab.cs")]
    [InlineData("src/Features", "src/Features/Hooks/a.cs")]
    [InlineData("src/*/a.cs", "src/x/*.cs")]
    [InlineData("src/Hooks/**", "src/Hooks/")]
    public void Globs_that_can_name_the_same_file_overlap(string first, string second)
    {
        Assert.True(OwnsGlob.Overlap(first, second));
        Assert.True(OwnsGlob.Overlap(second, first));
    }

    [Theory]
    [InlineData("src/a.cs", "src/b.cs")]
    [InlineData("src/Hooks/**", "src/Agents/**")]
    [InlineData("src/*.cs", "src/a.json")]
    [InlineData("src/*.cs", "src/x/a.cs")]
    [InlineData("src/*", "src/x/a.cs")]
    [InlineData("tests/**", "src/**")]
    public void Globs_that_cannot_name_the_same_file_do_not_overlap(string first, string second)
    {
        Assert.False(OwnsGlob.Overlap(first, second));
        Assert.False(OwnsGlob.Overlap(second, first));
    }
}
