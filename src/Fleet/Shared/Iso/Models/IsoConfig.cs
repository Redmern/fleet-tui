namespace Fleet.Shared.Iso.Models;

public sealed record IsoConfig(bool On, IReadOnlyList<string> AttachFrom, IReadOnlyDictionary<string, string> Codes)
{
    public static IsoConfig Off { get; } = new(false, [], new Dictionary<string, string>());

    public static IsoConfig Unreadable { get; } = new(true, [], new Dictionary<string, string>());

    public bool MayAttachFrom(string? origin) =>
        origin is { Length: > 0 }
        && AttachFrom.Any(h => string.Equals(h.Trim(), origin.Trim(), StringComparison.OrdinalIgnoreCase));
}
