namespace Fleet.Shared.Harness;

[Flags]
public enum HarnessCapabilities
{
    None = 0,
    Mcp = 1,
    Hooks = 2,
    Skills = 4,
    Permissions = 8,
    NativeMessaging = 16,
}
