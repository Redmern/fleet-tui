using Fleet.Shared.Viewing.Enums;

namespace Fleet.Shared.Viewing.Models;

public sealed record ViewedFrom(ViewKind Kind, string Name)
{
    public const string Variable = "FLEET_VIEWED_FROM";

    private const string ViewPrefix = "view:";

    private const string SshPrefix = "ssh:";

    public string Mark => (Kind == ViewKind.Ssh ? SshPrefix : ViewPrefix) + Name;

    public static ViewedFrom? Parse(string? mark) => mark switch
    {
        { Length: > 0 } m when m.StartsWith(ViewPrefix, StringComparison.Ordinal) && m.Length > ViewPrefix.Length =>
            new ViewedFrom(ViewKind.View, m[ViewPrefix.Length..]),
        { Length: > 0 } m when m.StartsWith(SshPrefix, StringComparison.Ordinal) && m.Length > SshPrefix.Length =>
            new ViewedFrom(ViewKind.Ssh, m[SshPrefix.Length..]),
        _ => null,
    };
}
