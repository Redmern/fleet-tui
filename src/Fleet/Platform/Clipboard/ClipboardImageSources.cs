using Fleet.Ports;

namespace Fleet.Platform.Clipboard;

public static class ClipboardImageSources
{
    public static IClipboardImageSource ForThisMachine(int maxBytes) =>
        OperatingSystem.IsLinux() ? new LinuxClipboardImageSource(maxBytes) : new NoClipboardImage();
}
