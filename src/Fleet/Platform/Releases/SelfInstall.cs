using System.Runtime.InteropServices;
using Fleet.Ports.Releases;

namespace Fleet.Platform.Releases;

public sealed class SelfInstall : IBinaryInstaller
{
    public void Replace(string targetPath, byte[] content)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ReplaceOnWindows(targetPath, content);
            return;
        }

        var temp = $"{targetPath}.new-{Guid.NewGuid():N}";

        File.WriteAllBytes(temp, content);
        File.SetUnixFileMode(
            temp,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        File.Move(temp, targetPath, overwrite: true);
    }

    private static void ReplaceOnWindows(string targetPath, byte[] content)
    {
        var stale = $"{targetPath}.old-{DateTime.UtcNow:yyyyMMddHHmmss}";

        try
        {
            File.Move(targetPath, stale, overwrite: true);
        }
        catch (IOException)
        {
        }

        File.WriteAllBytes(targetPath, content);

        var directory = Path.GetDirectoryName(targetPath);

        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        foreach (var old in Directory.GetFiles(directory, $"{Path.GetFileName(targetPath)}.old-*"))
        {
            try
            {
                File.Delete(old);
            }
            catch (IOException)
            {
            }
        }
    }
}
