using System.Runtime.InteropServices;

namespace Fleet.Shared.Releases;

public static class ReleaseAssetNames
{
    public static string? ForCurrentPlatform() => For(CurrentOS(), RuntimeInformation.OSArchitecture);

    public static string? For(OSPlatform os, Architecture arch)
    {
        if (arch != Architecture.X64)
        {
            return null;
        }

        if (os == OSPlatform.Windows)
        {
            return "fleet-win-x64.exe";
        }

        if (os == OSPlatform.Linux)
        {
            return "fleet-linux-x64";
        }

        return null;
    }

    private static OSPlatform CurrentOS()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return OSPlatform.Windows;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return OSPlatform.Linux;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return OSPlatform.OSX;
        }

        return OSPlatform.Create("unknown");
    }
}
