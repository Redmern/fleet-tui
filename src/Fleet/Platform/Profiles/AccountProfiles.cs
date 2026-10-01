using Fleet.Shared;

namespace Fleet.Platform.Profiles;

public sealed record AccountProfile(string Name, IReadOnlyList<string> Aliases, IReadOnlyList<string> Roots, string Claude);

public sealed class AccountProfiles(string? defaultName, IReadOnlyList<AccountProfile> profiles)
{
    public const string ConfigDirVariable = "CLAUDE_CONFIG_DIR";

    public const string PinnedVariable = "ACCOUNT_PROFILE";

    public const string AutoVariable = "ACCOUNT_PROFILE_AUTO";

    public static string DefaultFile =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".profiles.psd1");

    private static string ClaudeDefaultDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

    public IReadOnlyList<AccountProfile> All => profiles;

    public static AccountProfiles Parse(string text)
    {
        if (PowerShellData.Parse(text) is not Dictionary<string, object?> data)
        {
            throw new FormatException("the file does not hold a @{ } table");
        }

        var list = new List<AccountProfile>();
        foreach (var entry in Items(data.GetValueOrDefault("Profiles")).OfType<Dictionary<string, object?>>())
        {
            if (entry.GetValueOrDefault("Name") is not string name || name.Length == 0)
            {
                continue;
            }

            list.Add(new AccountProfile(
                name,
                [.. Items(entry.GetValueOrDefault("Aliases")).OfType<string>()],
                [.. Items(entry.GetValueOrDefault("Roots")).OfType<string>().Select(r => r.TrimEnd('\\', '/'))],
                Expand(entry.GetValueOrDefault("Claude") as string ?? string.Empty)));
        }

        return new AccountProfiles(data.GetValueOrDefault("Default") as string, list);
    }

    public AccountProfile? Named(string name) =>
        profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
            || p.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase)));

    public AccountProfile? ForFolder(string path) =>
        profiles
            .SelectMany(p => p.Roots.Select(r => (Profile: p, Root: r)))
            .Where(x => x.Root.Length > 0 && PathKey.Within(path, x.Root))
            .OrderByDescending(x => x.Root.Length)
            .Select(x => x.Profile)
            .FirstOrDefault()
        ?? (defaultName is null ? null : Named(defaultName));

    public static IReadOnlyDictionary<string, string> PinnedEnv(AccountProfile profile) =>
        new Dictionary<string, string>
        {
            [ConfigDirVariable] = ConfigDir(profile) ?? string.Empty,
            [PinnedVariable] = profile.Name,
            [AutoVariable] = string.Empty,
        };

    public static IReadOnlyDictionary<string, string> FolderEnv(AccountProfile? profile) =>
        ConfigDir(profile) is { } dir
            ? new Dictionary<string, string>
            {
                [ConfigDirVariable] = dir,
                [PinnedVariable] = string.Empty,
                [AutoVariable] = "1",
            }
            : new Dictionary<string, string>
            {
                [ConfigDirVariable] = string.Empty,
                [PinnedVariable] = string.Empty,
                [AutoVariable] = string.Empty,
            };

    public static string? ConfigDir(AccountProfile? profile) =>
        profile is { Claude.Length: > 0 } && !PathKey.Same(profile.Claude, ClaudeDefaultDir) ? profile.Claude : null;

    private static IEnumerable<object?> Items(object? value) => value switch
    {
        List<object?> list => list,
        null => [],
        _ => [value],
    };

    private static string Expand(string path) =>
        (path.StartsWith('~')
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[1..].TrimStart('\\', '/').Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar))
            : path).TrimEnd('\\', '/');
}
