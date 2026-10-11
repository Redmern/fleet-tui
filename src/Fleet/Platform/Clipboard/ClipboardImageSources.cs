using Fleet.Ports;

namespace Fleet.Platform.Clipboard;

public static class ClipboardImageSources
{
    public static IClipboardImageSource ForThisMachine() =>
        OperatingSystem.IsLinux() ? new LinuxClipboardImageSource() : new NoClipboardImage();
}
