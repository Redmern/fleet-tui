using Fleet.Platform.Claude;
using Fleet.Platform.Keybinds;

namespace Fleet.Tests.Platform.Claude;

public sealed class ClaudeConfigHomeTests
{
    [Fact]
    public void An_explicit_CLAUDE_CONFIG_DIR_wins()
    {
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", @"C:\custom\claude");

        try
        {
            Assert.Equal(
                @"C:\custom\claude",
                ClaudeConfigHome.ForFolder(Path.GetTempPath(), @"C:\home"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous);
        }
    }

    [Fact]
    public void An_ordinary_folder_falls_back_to_the_default_home()
    {
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", null);

        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "fleet-tests", Path.GetRandomFileName());
            Directory.CreateDirectory(folder);

            Assert.Equal(@"C:\home", ClaudeConfigHome.ForFolder(folder, @"C:\home"));

            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous);
        }
    }

    [Fact]
    public void Keybind_targets_get_each_resolved_home_once()
    {
        var previous = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", @"C:\custom\claude\");

        try
        {
            Assert.Equal(
                [@"C:\custom\claude"],
                KeybindTargetPaths.ClaudeHomes([Path.GetTempPath(), Environment.CurrentDirectory], @"C:\home"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR", previous);
        }
    }
}
