using Fleet.Platform.Storage;
using Fleet.Ports;

namespace Fleet.Platform.Logging;

public sealed class FileLog : IFleetLog
{
    public void Swallowed(Exception e) =>
        Write($"swallowed: {e.GetType().Name}: {e.Message}{Detail(e)}");

    private static string Detail(Exception e)
    {
        var lines = (e.StackTrace ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(12)
            .Select(l => $"{Environment.NewLine}    {l}");

        return string.Concat(lines);
    }

    public void Write(string line)
    {
        try
        {
            FleetPaths.EnsureDirs();
            File.AppendAllText(
                FleetPaths.LogFile,
                $"{DateTimeOffset.Now:O} {line}{Environment.NewLine}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public IReadOnlyList<string> Tail(int lines)
    {
        try
        {
            return File.ReadLines(FleetPaths.LogFile).TakeLast(lines).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
