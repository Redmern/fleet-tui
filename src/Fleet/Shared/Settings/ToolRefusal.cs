using Fleet.Shared.Settings.Enums;
using Fleet.Shared.Settings.Models;

namespace Fleet.Shared.Settings;

public static class ToolRefusal
{
    public static string? Forbidden(string project, SettingsConfig config, HarnessTool tool) =>
        config.RuleFor(tool).Policy == ActionPolicy.Forbid
            ? $"{project} does not allow {HarnessToolIds.For(tool)}; change it in that project's permissions."
            : null;
}
