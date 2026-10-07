namespace Fleet.Shared.Keybinds;

public static class KeybindDefaults
{
    public const string ResourceName = "keybinds.default.json";

    private static readonly Lazy<KeybindSet> Shipped = new(Load);

    public static KeybindSet Set => Shipped.Value;

    public static string Json()
    {
        using var stream = typeof(KeybindDefaults).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded in fleet");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static KeybindSet Load()
    {
        var problems = new List<string>();
        var set = KeybindLayering.Apply(KeybindSet.Empty, KeybindLayering.Parse(Json()).Keybinds, problems.Add);

        return problems.Count == 0
            ? set
            : throw new InvalidOperationException($"{ResourceName} is invalid: {string.Join("; ", problems)}");
    }
}
