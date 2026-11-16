namespace Fleet.Shared.Status;

public static class ClosingMark
{
    public const string FileName = "closing";

    public static void Write(string folder)
    {
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            var path = PathIn(folder);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DateTime.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    public static bool Exists(string folder) => folder.Length > 0 && File.Exists(PathIn(folder));

    public static string? FolderCovering(string path)
    {
        for (var folder = path.Length == 0 ? null : path; !string.IsNullOrEmpty(folder); folder = Path.GetDirectoryName(folder))
        {
            if (Exists(folder))
            {
                return folder;
            }
        }

        return null;
    }

    public static void Clear(string folder)
    {
        if (folder.Length == 0)
        {
            return;
        }

        try
        {
            File.Delete(PathIn(folder));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
        }
    }

    private static string PathIn(string folder) => Path.Combine(folder, ".fleet", FileName);
}
