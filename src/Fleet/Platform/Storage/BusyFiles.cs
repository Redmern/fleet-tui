namespace Fleet.Platform.Storage;

public static class BusyFiles
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    public static T? Retry<T>(Func<T> attempt, TimeSpan patience)
        where T : class
    {
        var deadline = DateTime.UtcNow + patience;

        while (true)
        {
            try
            {
                return attempt();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    return null;
                }

                Thread.Sleep(15);
            }
        }
    }

    public static bool Replace(string file, Action<string> writeTemp)
    {
        var temp = file + ".tmp";

        return Retry(() =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            writeTemp(temp);
            File.Move(temp, file, overwrite: true);
            return file;
        }, Patience) is not null;
    }
}
