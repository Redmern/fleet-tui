using Fleet.Platform.Themes;
using Fleet.Ports.Keybinds;
using Fleet.Shared.Keybinds;
using Fleet.Shared.Keybinds.Enums;

namespace Fleet.Platform.Nvim;

public sealed class NvimKeybindFiles(string configDirectory, KeybindOs? os = null) : INvimKeybinds
{
    private readonly KeybindOs _os = os ?? KeybindNames.CurrentOs;

    public string GeneratedFile => Path.Combine(configDirectory, "lua", "fleet", NvimKeybinds.GeneratedFile);

    public bool WriteGenerated(KeybindSet set) => Write(GeneratedFile, Generated(set));

    public bool WriteUserModule(KeybindSet set, string path) => Write(path, UserModule(set));

    public bool GeneratedIsCurrent(KeybindSet set) => Holds(GeneratedFile, Generated(set));

    public bool UserModuleIsCurrent(KeybindSet set, string path) => Holds(path, UserModule(set));

    private string Generated(KeybindSet set) => NvimKeybinds.Generate(set.For(KeybindTarget.Nvim, _os));

    private string UserModule(KeybindSet set) => NvimKeybinds.GenerateUserModule(set.For(KeybindTarget.Nvim, _os));

    private static bool Holds(string path, string lua)
    {
        try
        {
            return File.Exists(path) && File.ReadAllText(path) == lua;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static bool Write(string path, string lua)
    {
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == lua)
            {
                return true;
            }

            FileThemeStore.WriteAtomically(Path.GetFullPath(path), lua);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }
}
