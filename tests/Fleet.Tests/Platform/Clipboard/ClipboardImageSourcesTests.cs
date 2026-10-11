using Fleet.Platform.Clipboard;

namespace Fleet.Tests.Platform.Clipboard;

public class ClipboardImageSourcesTests
{
    [Fact]
    public void This_machine_gets_the_reader_of_its_own_operating_system()
    {
        var source = ClipboardImageSources.ForThisMachine(1024);

        var expected = OperatingSystem.IsLinux() ? typeof(LinuxClipboardImageSource)
            : OperatingSystem.IsWindows() ? typeof(WindowsClipboardImageSource)
            : OperatingSystem.IsMacOS() ? typeof(MacClipboardImageSource)
            : typeof(NoClipboardImage);
        Assert.IsType(expected, source);
    }
}
