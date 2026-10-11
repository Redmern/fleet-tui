using Fleet.Ports;

namespace Fleet.Platform.Clipboard;

public static class ClipboardImageSources
{
    public static IClipboardImageSource ForThisMachine(int maxBytes) =>
        OperatingSystem.IsLinux() ? new LinuxClipboardImageSource(maxBytes)
        : OperatingSystem.IsWindows() ? new WindowsClipboardImageSource(maxBytes)
        : OperatingSystem.IsMacOS() ? new MacClipboardImageSource(maxBytes)
        : new NoClipboardImage();
}
