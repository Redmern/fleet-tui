using System.Text.Json;
using Fleet.Platform.Storage.Models;
using Fleet.Ports.Notifications;
using Fleet.Ports.Notifications.Enums;
using Fleet.Ports.Notifications.Models;
using Fleet.Shared;

namespace Fleet.Platform.Storage;

public sealed class JsonNoticeStore : INoticeStore
{
    private const string SettingsName = "_settings.json";

    public IReadOnlyList<Notice> Load(string project)
    {
        if (FileFor(project) is not { } file || !File.Exists(file))
        {
            return [];
        }

        var loaded = BusyFiles.Retry(() => File.ReadAllText(file), TimeSpan.FromMilliseconds(200));
        if (loaded is null)
        {
            return [];
        }

        try
        {
            var stored = JsonSerializer.Deserialize(loaded, FleetJsonContext.Default.NoticeFile) ?? new NoticeFile();
            return [.. stored.Notices
                .Where(n => Enum.TryParse<NoticeKind>(n.Kind, out _))
                .Select(n => new Notice(
                    project,
                    Enum.Parse<NoticeKind>(n.Kind),
                    HomePath.Expand(n.Worktree),
                    n.Agent,
                    n.Message,
                    n.Since,
                    n.Resolved,
                    n.Dismissed))];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public void Save(string project, IReadOnlyList<Notice> notices)
    {
        if (FileFor(project) is not { } file)
        {
            return;
        }

        var json = JsonSerializer.Serialize(
            new NoticeFile
            {
                Notices = [.. notices.Select(n => new NoticeEntry
                {
                    Kind = n.Kind.ToString(),
                    Worktree = HomePath.Contract(n.Worktree),
                    Agent = n.Agent,
                    Message = n.Message,
                    Since = n.Since,
                    Resolved = n.Resolved,
                    Dismissed = n.Dismissed,
                })],
            },
            FleetJsonContext.Default.NoticeFile);

        BusyFiles.Replace(file, temp => File.WriteAllText(temp, json));
    }

    public IReadOnlyList<string> Projects() =>
        Directory.Exists(FleetPaths.Notices)
            ? [.. Directory.EnumerateFiles(FleetPaths.Notices, "*.json")
                .Where(f => !Path.GetFileName(f).Equals(SettingsName, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Order(StringComparer.OrdinalIgnoreCase)]
            : [];

    public NoticeSettings Settings()
    {
        var file = Path.Combine(FleetPaths.Notices, SettingsName);

        try
        {
            if (File.Exists(file)
                && JsonSerializer.Deserialize(File.ReadAllText(file), FleetJsonContext.Default.NoticeSettingsFile) is { } read)
            {
                return new NoticeSettings(read.Bell, read.Toast);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }

        return new NoticeSettings();
    }

    public void Save(NoticeSettings settings)
    {
        var json = JsonSerializer.Serialize(
            new NoticeSettingsFile { Bell = settings.Bell, Toast = settings.Toast },
            FleetJsonContext.Default.NoticeSettingsFile);

        BusyFiles.Replace(Path.Combine(FleetPaths.Notices, SettingsName), temp => File.WriteAllText(temp, json));
    }

    private static string? FileFor(string project)
    {
        var name = ProjectName.Sanitize(project);

        return name.Length == 0 ? null : Path.Combine(FleetPaths.Notices, name + ".json");
    }
}
