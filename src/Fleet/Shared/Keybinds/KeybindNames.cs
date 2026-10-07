using System.Text;
using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Shared.Keybinds;

public static class KeybindNames
{
    private static readonly IReadOnlyDictionary<string, KeybindTarget> Targets =
        new Dictionary<string, KeybindTarget>(StringComparer.OrdinalIgnoreCase)
        {
            ["fleet-ui"] = KeybindTarget.FleetUi,
            ["mux"] = KeybindTarget.Mux,
            ["nvim"] = KeybindTarget.Nvim,
            ["claude"] = KeybindTarget.Claude,
        };

    private static readonly IReadOnlyDictionary<string, KeybindOs> Systems =
        new Dictionary<string, KeybindOs>(StringComparer.OrdinalIgnoreCase)
        {
            ["windows"] = KeybindOs.Windows,
            ["linux"] = KeybindOs.Linux,
            ["macos"] = KeybindOs.MacOs,
        };

    public static KeybindOs CurrentOs =>
        OperatingSystem.IsWindows() ? KeybindOs.Windows
        : OperatingSystem.IsMacOS() ? KeybindOs.MacOs
        : KeybindOs.Linux;

    public static bool TryTarget(string name, out KeybindTarget target) =>
        Targets.TryGetValue(name.Trim(), out target);

    public static bool TryOs(string name, out KeybindOs os) => Systems.TryGetValue(name.Trim(), out os);

    public static string Of(KeybindTarget target) => Targets.First(t => t.Value == target).Key;

    public static string Of(KeybindOs os) => Systems.First(s => s.Value == os).Key;

    public static string Kebab(string pascal)
    {
        var builder = new StringBuilder(pascal.Length + 8);

        for (var i = 0; i < pascal.Length; i++)
        {
            var c = pascal[i];

            if (char.IsUpper(c) && i > 0)
            {
                builder.Append('-');
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
