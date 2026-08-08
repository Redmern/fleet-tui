using Fleet.Shared;

namespace Fleet.Tests.Shared;

public class ProjectNameTests
{
    [Theory]
    [InlineData("backend", "backend")]
    [InlineData("My Project", "MyProject")]
    [InlineData("a/b\\c", "abc")]
    [InlineData("web-app_2", "web-app_2")]
    [InlineData("...", "")]
    public void Sanitize_keeps_only_filename_safe_characters(string input, string expected)
        => Assert.Equal(expected, ProjectName.Sanitize(input));

    [Theory]
    [InlineData("..")]
    [InlineData("../../etc")]
    [InlineData("C:\\Windows")]
    public void Sanitize_cannot_produce_a_path_traversal(string input)
    {
        var result = ProjectName.Sanitize(input);

        Assert.DoesNotContain("..", result);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, result);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, result);
    }
}
