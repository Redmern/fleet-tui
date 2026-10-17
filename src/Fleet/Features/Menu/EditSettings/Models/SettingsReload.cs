using Fleet.Shared.Settings.Models;

namespace Fleet.Features.Menu.EditSettings.Models;

public sealed record SettingsReload(SettingsConfig Config, string Message);
