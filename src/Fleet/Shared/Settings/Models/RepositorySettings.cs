using Fleet.Shared.Settings.Enums;

namespace Fleet.Shared.Settings.Models;

public sealed record RepositorySettings(ForgeKind Forge, string PushRemote)
{
    public const string Section = "repositories";

    public const string ForgeKey = "forge";

    public const string PushRemoteKey = "pushRemote";

    public const string DefaultPushRemote = "origin";

    public static RepositorySettings Default { get; } = new(ForgeKind.Auto, DefaultPushRemote);

    public static IReadOnlyList<ForgeKind> Forges { get; } = [ForgeKind.GitHub, ForgeKind.Forgejo, ForgeKind.Gitea];

    public static string ForgeWords { get; } = string.Join(", ", Forges.Select(Word));

    public bool IsDefault => Forge == ForgeKind.Auto && Remote == DefaultPushRemote;

    public string Remote => PushRemote.Trim().Length == 0 ? DefaultPushRemote : PushRemote.Trim();

    public static string Word(ForgeKind forge) => forge switch
    {
        ForgeKind.GitHub => "github",
        ForgeKind.Forgejo => "forgejo",
        ForgeKind.Gitea => "gitea",
        _ => string.Empty,
    };

    public static ForgeKind ParseForge(string? word)
    {
        var wanted = (word ?? string.Empty).Trim();

        foreach (var forge in Forges)
        {
            if (string.Equals(Word(forge), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return forge;
            }
        }

        return ForgeKind.Auto;
    }

    public static RepositorySettings From(string? forge, string? pushRemote) =>
        new(ParseForge(forge), (pushRemote ?? string.Empty).Trim() is { Length: > 0 } remote ? remote : DefaultPushRemote);

    public static string ForgeSetting(string repository) => $"{Section}.{repository}.{ForgeKey}";

    public static string PushRemoteSetting(string repository) => $"{Section}.{repository}.{PushRemoteKey}";
}
