using Fleet.Ports.Themes.Enums;
using Fleet.Shared.Themes;

namespace Fleet.Features.Themes.ManageThemes.Models;

public sealed record OmarchySetup(HookInstall Hook, string HookFile, ThemePalette? Theme, string? SyncError);
